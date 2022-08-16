# Pre-ingest API

Een .NET (Core) gebaseerde REST API service met het doel om diverse controle & validatie acties uit te voeren om de kwaliteit van de aangeleverde collecties te beoordelen.

## Controllers
De Preingest REST API bestaat uit 6 contollers. Elk controller heeft één of meerdere acties om uit te voeren. De controllers zijn:

- Preingest
- Output
- Service
- Status
- Opex
- ToPX naar MDTO

### Preingest
- CollectionChecksumCalculation: berekenen van een checksum waarde volgens een algoritme. Algoritme MD5, SHA1, SHA-256 en SHA512 wordt standaard via .NET uitgerekend. Voor SHA-224 en SHA-384 wordt de calculatie gedaan met behulp van [een onderliggende service](https://github.com/noord-hollandsarchief/preingest-mdto-utilities).

- Unpack: uitpakken van een collectie. Een collectie is in een TAR formaat opgeleverd. De collecties dienen opgeslagen te zijn in `data` map.

- VirusScan: een uitgepakte collectie scannen voor virus en/of malware. De actie maakt gebruikt van [een onderliggende service](https://hub.docker.com/r/clamav/clamav). 

- Naming: naamgeving van mappen en bestanden binnen een collectie controleren op bijzonder karakters, ongewenste combinaties en de maximale lengte van een naam.

- Sidecar: structuur van een collectie controlleren op de constructie (volgens de sidecar principe), de opbouw van aggregatie niveau's bij ToPX en MDTO, mappen zonder bestanden en de uniciteit van de aangeleverde objecten.

- Profiling: Voor het classificeren van mappen en bestanden binnen een collectie is een profiel nodig. Nadat een profiel is aangemaakt kunnen de acties Exporting en Reporting gestart worden. Het aanmaken van een profiel wordt gedaan m.b.v. [een onderliggende service](https://github.com/noord-hollandsarchief/preingest-droid).

- Exporting: De resultaten bij het classificeren van mappen en bestanden binnen een collectie opslaan als een CSV bestand. Het exporteren van de resultaten wordt gedaan m.b.v. [een onderliggende service](https://github.com/noord-hollandsarchief/preingest-droid).

- Reporting: De resultaten bij het classificeren van mappen en bestanden binnen een collectie opslaan als een PDF bestand. Het opslaan van de resultaten wordt gedaan m.b.v. [een onderliggende service](https://github.com/noord-hollandsarchief/preingest-droid)

- SignatureUpdate: Interne classificatie lijst van DROID bijwerken. Zie [DROID](https://www.nationalarchives.gov.uk/information-management/manage-information/preserving-digital-records/droid/) voor meer informatie.

- Greenlist: Bestanden binnen een collectie vergelijken met een voorkeurslijst. Voorkeurslijst is een overzicht met bestand extensies en formaten die NHA een primaire voorkeur om te ingesten. De actie wordt gedaan m.b.v. [een onderliggende service](https://github.com/noord-hollandsarchief/preingest-mdto-utilities).

- Encoding: ToPX of MDTO metadata bestanden controleren op encoding en byte order mark.

- ValidateMetadata: ToPX of MDTO metadata bestanden valideren volgens de XSD schema's en controleren op business regels volgens de NHA specificaties bijv. beperking gebruik, openbaarheid en auteurswet. De actie wordt (deels) gedaan m.b.v. [een onderliggende service](https://github.com/noord-hollandsarchief/preingest-xslweb).

- CreateExcel: De Preingest resultaten van alle uitgevoerde acties ophalen, converteren en opslaan als een MS Excel bestand.

- PutSettings: Instellingen opslaan van de tool.

- PreWashMetadata: Mogelijkheid om ToPX of MDTO metadata bestanden bij te werken d.m.v. XSLT transformatie. Hiervoor moet wel XSLT bestanden toegevoegd worden met specifieke transformatie. De actie wordt gedaan m.b.v. [een onderliggende service](https://github.com/noord-hollandsarchief/preingest-xslweb).

- IndexMetadataFiles: Alle elementen en waarde van ToPX of MDTO metadata bestanden binnen een collectie extraheren en opslaan in een MS Excel bestand.

- DetectPasswordProtection: Het achterhalen van wachtwoorden binnen MS Office en PDF bestanden. De actie wordt gedaan m.b.v. [een onderliggende service](https://github.com/noord-hollandsarchief/preingest-mdto-utilities). 

- UpdateWithPronom: ToPX of MDTO metadata bestanden van het type 'bestand' bijwerken met informatie uit de classificatie resultaten. Deze actie vereist een resultaat van 'Exporting'. 

- ValidateBinaries: Binaire bestanden binnen een collectie controleren en vergelijken met de classificatie resultaten. Deze actie vereist een resultaat van 'Exporting'.

### Output
### Service
### Status
### Opex
### ToPX naar MDTO

## OpenAPI (Swagger)
Voor een volledige REST-API specificaties van de Preingest tool, start de service en ga naar http://[servername][port]/swagger/index.html.

## Configuraties

## SignalR (websocket)

## Docker



