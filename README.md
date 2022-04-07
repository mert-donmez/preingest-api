# Noord-Hollands Archief pre-ingest tooling

A collection of tools for validation and transformation, to support transferring TMLO/ToPX archives to
[the NHA Preservica e-Depot](https://noord-hollandsarchief.nl/informatiebeheer/e-depot).

For details, [please contact us](mailto:preservica@noord-hollandsarchief.nl).

This expects `.tar` or `.tar.gz` archives confirming to the Dutch National Archives TMLO/ToPX sidecar format. Once put
into the configured input folder, the API [or frontend](https://github.com/noord-hollandsarchief/preingest-frontend) can
be used to configure the file details (such as the expected checksum and details about the target location), after which
validations and transformations can be scheduled. When all fine, Preservica's SIP Creator will be invoked to create a
XIP v4 SIP package, which will be moved to Preservica's Transfer Agent's upload folder. 


## Available actions

The following validation and transformation actions are currently implemented:

- Calculate and validate the archive's MD-5, SHA-1, SHA-256 or SHA-512 checksum.
- Extract the `.tar` or `.tar.gz` archive.
- Check for viruses. This [uses ClamAV](https://hub.docker.com/r/mkodockx/docker-clamav) and updates its definitions
  automatically. It also scans compressed archives, OLE2 documents and email files.
- [Perform "pre-wash" custom XSLT transformations](prewash) to fix errors or enrich the incoming ToPX data, especially
  during testing.
- [Validate the file names](preingest/Noord.HollandsArchief.Pre.Ingest.WebApi/Handlers/NamingValidationHandler.cs), to
  not contain invalid characters, and not be Windows/DOS-reserved names such as `PRN` or `AUX`.
- [Validate the TMLO/ToPX sidecar structure](preingest/Noord.HollandsArchief.Pre.Ingest.WebApi/Handlers/SidecarValidationHandler.cs).
- Classify the file types, to determine PRONOM IDs and to see if the extension matches the actual content. This 
  [uses DROID](droid-validation/README.md) and does NOT currently automatically update its file database.
- Export the DROID results to CSV, XML or PDF. The results are also included in the final Excel report.
- [Compare the files types](preingest/Noord.HollandsArchief.Pre.Ingest.WebApi/Handlers/GreenListHandler.cs) against
  the [list of preferred formats](preingest/Noord.HollandsArchief.Pre.Ingest.WebApi/Datasource/greenlist.json).
- [Validate the file encoding](preingest/Noord.HollandsArchief.Pre.Ingest.WebApi/Handlers/EncodingHandler.cs) to always
  be UTF8.
- [Validate the sidecar files](xslweb-jdk8-webservices-docker/home/webapps/topxvalidation/xsl/request-dispatcher.xsl)
  against the ToPX XSD and [Schematron rules](xslweb-jdk8-webservices-docker/home/webapps/topxvalidation/sch/topx.sch).
  Some of the Schematron rules may be specific to the requirements of the Noord-Hollands Archief.
- [Transform the ToPX metadata](xslweb-jdk8-webservices-docker/home/webapps/transform/xsl/topx2xip.xslt) files to
  Preservica XIP v4. This also maps ToPX `<omschrijvingBeperkingen>` to Preservica security tags, specific to the
  requirements of the Noord-Hollands Archief, and defaulting to `closed` if missing (which will have been reported
  during validation).
- Run SIP Creator 5.11 to transform XIP v4 to Preservica SIP. This uses the XIP fragments from the many sidecar metadata
  files (as transformed in the previous step) to create a single `metadata.xml` file. When done, the pre-ingest creates
  a ZIP archive for the resulting Preservica SIP directory structure, `<name>.sip.zip`, including that `metadata.xml`
  (but not the sidecar files) and the actual assets.
- [Validate the SIP Creator result](xslweb-jdk8-webservices-docker/home/webapps/xipvalidation/xsl/request-dispatcher.xsl)
  against `XIP-V4.xsd`.
- [Create an overall Excel report](xslweb-jdk8-webservices-docker/home/webapps/excelreport/xsl/request-dispatcher.xsl).
- [Copy the SIP Creator result](preingest/Noord.HollandsArchief.Pre.Ingest.WebApi/Handlers/SipZipCopyHandler.cs) to the
  Transfer Agent.

Whenever a validation fails, its result is set to Error, regardless the severity. If any error occurred in the currently
scheduled actions, then the action to copy the SIP to the Transfer Agent area will not be started but marked as Failed.
This can be ignored by re-scheduling that very action again.

Multiple files can be handled in parallel, but for each file the API simply executes any requested action sequentially.
The frontend will enforce a specific order and take care of scheduling dependencies. The frontend will also allow some
actions to be re-scheduled any time, but most actions cannot be run again once they succeeded.

## Preservica XIP vs OPEX

The current version generates temporary XIP v4 fragments, which have some limitations during the further processing:

- Despite SIP Creator copying the values into its final `metadata.xml`, during ingest XIP `<Title>` and `<Description>`
  are ignored for `<File>`
- `<Description>` is also ignored for `<Collection>` (so is only handled for `<DeliverableUnit>`)
- `<File>` does not support security tags

Future versions will probably use OPEX instead, also nicely taking the away the need for SIP Creator (and probably using
some direct upload to AWS S3 buckets rather than using the Windows-only Transfer Agent).

## Overall status

Overall result while processing an archive (collection):

- `Running` if any action is currently running, else:
- `Failed` if any of the actions reported a fatal error, else:
- `Error` if any of the actions reported an error, else:
- `Success` if any action was executed and all returned Success, else:
- `New` if nothing happened for an archive yet.

As saving the settings is basically an action too, doing so will change the overall state from `New` to `Success`.

## Data folder structure and handling

A TMLO/ToPX Submission Information Package (SIP) is expected to be a `.tar` or `.tar.gz` file, and should be put into
the configured data folder.

For each new SIP a "session" folder is created in the data folder. The name looks like a GUID but is actually derived
from the file name. So, processing the same file name multiple times requires you to remove old results. The session id
is also used in the URLs of the frontend, like `/s/4b08bb60-52b3-0f22-5103-3a34b1daf56b`.

The TAR archive is unpacked in the session folder, so a TMLO/ToPX SIP should include a root folder.

The transformations change the ToPX metadata files, so (except for "pre-wash" custom transformations) often can only be
executed once, even if some error occurred. The frontend will block actions that no longer make sense.

Transformations are invoked with a REST-like GET URL that includes the file name to process. Given that, transformations
read the current contents from disk themselves, but do not write the results back to disk. Instead, transformations
return the result as text, leaving it up to the caller (the orchestrating API) to overwrite the original file. If really
needed then pre-wash fixes could create new files too; see [the examples](prewash/README.md).

For new collections, Preservica SIP Creator will generate UUIDs (for `CollectionRef`) which need to be unique even for
different tenants on the same installation. The frontend will not allow for changing the target environment once SIP
Creator has been used.

## Database

A single-file SQLite database is used to keep track of the current processes. This only holds temporary data and will be
re-created on startup if needed. So, in case of failures:

- Stop the pre-ingest (Docker) processes
- Remove the database file (see `DBFOLDER` below)
- Remove all session folders from the data folder (see `DATAFOLDER` below)
- Restart the pre-ingest for archives that still need processing

The database [should NOT be stored on a network share](https://sqlite.org/forum/forumpost/33f1a3a91d?t=h). And to avoid
database errors you may want to exclude its working folder from any virus scanning as well.

## Requirements

- A recent version of Docker, supporting Linux containers. (On Windows, you may need to enable Hyper-V for that.) Given
  [the large number](https://github.com/mko-x/docker-clamav#memory) of virus signature definitions, this may need at
  least 3 GB of memory. (Alternatively, disable virus scanning; see [docker-compose.dev.yml](docker-compose.dev.yml).)

- Preservica [SIP Creator 5.11](https://usergroup.preservica.com/downloads/view.php?resource=SIP+Creator&version=5.11).
  Its command line interface will be invoked using 64 bits Java 8 from a Linux Docker container. (In Docker on Windows,
  this was tested using both the Linux 64 bits ZIP, and the Windows 64 bits ZIP downloads.) A bug in the SIP Creator CLI
  requires installation in a folder without spaces, at least during its first run. Beware that Client Installer 5.10 and
  5.11 do not include the SIP Creator command line interface. Also note that the original Linux `createsip` script is
  not used; instead [a custom version](xslweb-jdk8-webservices-docker/home/webapps/sipcreator/scripts/nha-createsip) is
  included in the Docker image. Beware that 5.10 [does not suffice](https://eu.preservica.com/sdb/help/documentation):
  
  > #### 7.6. XIP Metadata Fragments
  > 
  > As of version 5.11, users can use the bulk metadata option to specify and override XIP fields as well as just
  > attaching generic metadata. To do this, the .metadata file should contain XML in the XIP format and namespace. When
  > using this with XIP File fragments, this mechanism can also be used to verify pre-existing checksums/fixity values
  > at the point of SIP creation.

  This information (such as `<Title>` and `<SecurityTag>`) is silently discarded when using SIP Creator 5.10. 

- Preservica Transfer Agent, configured and ready to use. This has been tested using [Preservica Client Installer
  5.10](https://usergroup.preservica.com/downloads/view.php?resource=Client+Installer&version=5.10). Version 5.11
  may work too, but in February 2021 that included an incomplete Java runtime. Though this a Java application, it is
  only supported on Windows, and only for Cloud Edition, not for on-premise installations.
  
  To support both test and production environments, this needs to be running twice:

  - Install it using the Client Installer 5.10 wizard. You'll need a URL like `https://eu.preservica.com/sdb` and can
    likely ignore a warning saying _"Connection to Preservica has been re-directed. Please check the URL and re-try.
    This often happens if http is used instead of https"_.
    
  - After installing it once, duplicate the result folder in `C:\Preservica\Local\Transfer Agent` to, say,
    `C:\Preservica\Local\Transfer Agent Production`.
    
  - In the copy, adjust the configuration file `C:\Preservica\Local\Transfer Agent Production\conf\upload.properties`,
    to use a different upload folder (make sure it exists), use different credentials, and make the ActiveMQ broker use
    another dummy port (this is used by SIP Creator GUI to monitor progress of uploads, but is not used in our case):

    ```ini
    upload.root=C:/Preservica/Local/UploadArea-Production
    ...
    preservica.username=prod-user@example.com
    preservica.password=prod-password
    preservica.url=https://eu.preservica.com/sdb
    ...
    amq.broker.url=tcp://docker01:61613
    ```

  - In the copy, adjust `installService.bat` and run that file to use different names, like:

    ```cmd
    set SERVICE_NAME=TransferAgentProduction
    set PR_DISPLAYNAME=Preservica Transfer Agent Production
    ```

  - Aside, [note](https://usergroup.preservica.com/documentation/ce/6.2.1/html/SIPCreatorSUG.html):

    > The Transfer Agent upload area must be hosted on the same system as the Transfer Agent application. The amount of
    > free disk space required by the Transfer Agent depends on the maximum size of packages being uploaded at the same
    > time. At least three times the maximum size simultaneous uploads is recommended.
    >
    > Installation of the Preservica client applications under "Program Files" or "Program Files (x86)" is not
    > recommended due to access restrictions in place on these folders. If the client applications are installed in one
    > of these folders then all standard permissions (Modify, Read & execute, List folder contents, Read and Write) must
    > be granted to the Windows group "Authenticated Users"

- The [frontend web application](https://github.com/noord-hollandsarchief/preingest-frontend). Alternatively, without
  building anything yourself, you can use the included [docker-compose file](docker-compose.yml) like described below.

## Docker

The Docker images require a Docker runtime that supports Linux containers. (On Windows, you may need Hyper-V for that.)

Some Docker images expect folders like `/data`, `/db`, `/nha/SIP_Creator` and `/nha/tomcat-logs` to be mapped to a folder
on the Docker host. (Note that Docker does not support symbolic links, unless they're supposed to map to folders inside
the containers.) When using the [docker-compose file](docker-compose.yml), this needs some environment variables:

- Make the settings known in environment variables. Note that a SQLite database is used which [should NOT be stored on
  a network share](https://sqlite.org/forum/forumpost/33f1a3a91d?t=h). For example:
  
  - Create a `.env` file in the root of this project, holding lines like:
  
    ```env
    DATAFOLDER=/path/to/data-folder
    # The database MUST be stored in a folder on the local machine, not on a network share
    DBFOLDER=/local/path/to/database-folder
    SIPCREATORFOLDER=/path/to/sip-creator-installation-folder
    TOMCATLOGFOLDER=/path/to/tomcat-log-folder
    TRANSFERAGENTTESTFOLDER=/path/to/transfer-agent-test-folder
    TRANSFERAGENTPRODFOLDER=/path/to/transfer-agent-production-folder
    XSLWEBPREWASHFOLDER=/path/to/prewash-xml-stylesheets-folder
    ```

  - Or, `export` the values in your Linux environment:
    
    ```bash
    export DATAFOLDER=/path/to/data-folder
    # The database MUST be stored in a folder on the local machine, not on a network share
    export DBFOLDER=/local/path/to/database-folder
    export SIPCREATORFOLDER=/path/to/sip-creator-installation-folder
    export TOMCATLOGFOLDER=/path/to/tomcat-log-folder
    export TRANSFERAGENTTESTFOLDER=/path/to/transfer-agent-test-folder
    export TRANSFERAGENTPRODFOLDER=/path/to/transfer-agent-production-folder
    export XSLWEBPREWASHFOLDER=/path/to/prewash-xml-stylesheets-folder
    ```

  - Or, `set` the values in your Windows environment:
    
    ```cmd
    set DATAFOLDER=D:\path\to\data-folder
    rem The database MUST be stored in a folder on the local machine, not on a network share
    set DBFOLDER=D:\local\path\to\database-folder
    set SIPCREATORFOLDER=D:\path\to\sip-creator-installation-folder
    set TOMCATLOGFOLDER=D:\path\to\tomcat-log-folder
    set TRANSFERAGENTTESTFOLDER=D:\path\to\transfer-agent-test-folder
    set TRANSFERAGENTPRODFOLDER=D:\path\to\transfer-agent-production-folder
    set XSLWEBPREWASHFOLDER=D:\path\to\prewash-xml-stylesheets-folder
    ```

- To use a CIFS (SMB) share to some network folder, define it in `docker-compose.override.yml`:

  ```yaml
  # NOTE: The details below will ONLY be used when Compose creates the volume. When changing
  #       anything, first delete the volume:
  #
  #       docker-compose down
  #       docker volume rm preingest_cifs-data
  #
  # For options see also https://linux.die.net/man/8/mount.cifs (but not "credentials=filename";
  # see https://stackoverflow.com/a/67723209)
  #
  # See https://github.com/dotnet/runtime/issues/42790#issuecomment-817758887 for using "nobrl":
  #
  #       nobrl
  #       Do not send byte range lock requests to the server. This is necessary for certain
  #       applications that break with cifs style mandatory byte range locks (and most cifs
  #       servers do not yet support requesting advisory byte range locks).
  #
  volumes:
    cifs-data:
      driver: local
      driver_opts:
        type: cifs
        device: "//servername/path/to/data"
        o: "addr=servername,username=johndoe,password=s=cr3t,nobrl"
  ```

  Next, the name `cifs-data` can then be used in environment variables, like `DATAFOLDER=cfis-data`.

- To run all Docker containers, pulling the development images if needed, run:

  ```text
  docker-compose up
  ```

- To build new images locally, adjust [docker-compose.dev.yml](docker-compose.dev.yml) as needed, and use with:

  ```text
  docker-compose -f docker-compose.yml -f docker-compose.dev.yml up
  ```

  This expects [the frontend code](https://github.com/noord-hollandsarchief/preingest-frontend) to have been cloned into
  `../preingest-frontend`.

  Alternatively, copy that example `docker-compose.dev.yml` into a file `docker-compose.override.yml`, which will be
  used automatically when `docker-compose up` is used.
  
  When images already exist (like after running `docker-compose pull` or when built earlier), add `--build` to force a
  new build.

## Development

The API, scheduler and non-XML validations use Microsoft .NET Core, while XML validations and transformations use
[XSLWeb pipelines](https://github.com/Armatiek/xslweb). As Microsoft Excel files are basically ZIP archives with XML
files, the Excel report is created using an XML transformation.

All Dockerfiles ensure one can build any part without the need to install any specific development environment.

### OpenAPI (formerly Swagger)

When using `docker-compose.yml`, the internal API can be accessed from `localhost:8000`, like
<http://localhost:8000/api/preingest/check> to see the API's health status. An OpenAPI specification is available at
<http://localhost:8000/swagger/v1/swagger.json>, and a built-in Swagger UI at <http://localhost:8000/swagger>.

### SignalR/WebSocket

Three script samples are available for receiving status update/events:
- http://localhost:8000/events.html
- http://localhost:8000/collections.html
- http://localhost:8000/collection.html

## Known issues and troubleshooting

- An action never completes: if somehow the orchestrating API misses out on the completed signal of a delegated action,
  then that action may stay in its running state forever (and the frontend will just increase the elapsed time). As a
  quick fix, remove the results of the very file from the database and file system (in the frontend: select it in the
  overview page to see the options) and restart all processing of the file. If the same problem keeps occurring then
  please contact us.

- `clamav exited with code 137` and `/bootstrap.sh: line 35: 15 Killed clamd`: increase the memory for the Docker host.

- `Preparing SIP Creator library for first use` followed by `ls: cannot access 'plugins/com.tessella.sdb.core.xip.gui_*.jar':
  No such file or directory`: ensure there are no spaces in the path of `SIPCREATORFOLDER`.

- Transfer Agent Windows service fails to start, complaining that the JRE cannot be instantiated: make sure to use
  Client Installer 5.10 (but only for Transfer Agent, not for SIP Creator), or provide a working Java runtime yourself
  when using 5.11.

- `SQLite Error 14: 'unable to open database file'`

  - ensure the database file is NOT stored on a network share
  - exclude the database file (default: `$DBFOLDER/preingest.db`) from virus scanning

- `Access to the path '...' is denied` and zero-byte files are created on a CIFS/SMB network share: ensure to [include
   `nobrl` in the driver options](https://github.com/dotnet/runtime/issues/42790#issuecomment-817758887).

- SIP Creator throwing `ERROR: File found inside Collection, where only '.metadata' files are allowed`: ensure there
  are no hidden files in the session folder, such as Windows `Thumbs.db` or macOS `.DS_Store` files.

- SIP Creator throwing `Tried to parse XML as a Collection but got a {http://www.tessella.com/XIP/v4}DeliverableUnit`:
  this is likely not an actual error.

## Trademarks

Preservica™ is a trademark of [Preservica Ltd](https://preservica.com/). The Noord-Hollands Archief is not affiliated
with that organisation. 
