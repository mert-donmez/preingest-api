# Noord-Hollands Archief pre-ingest API

Een .NET (Core) gebaseerde REST API service met het doel om diverse controle & validatie acties uit te voeren om de kwaliteit van de aangeleverde collecties te bepalen. 

De service bestaat uit meerdere contollers. Bij elk controller bevat deze meerdere acties om uit te voeren. De controllers zijn:
- Preingest
- Output
- Service
- Status
- Opex
- ToPX naar MDTO

## Controllers

### Preingest
- CollectionChecksumCalculation: berekenen van een checksum waarde volgens een algoritme. Algoritme MD5, SHA1, SHA-256 en SHA512 wordt standaard via .NET uitgerekend. Voor SHA-224 en SHA-384 wordt de calculatie gedaan met behulp van [een onderliggende service](https://github.com/noord-hollandsarchief/preingest-mdto-utilities).

- Unpack: uitpakken van een collectie. Een collectie is in een TAR formaat opgeleverd. De collecties dienen opgeslagen te zijn in `data` map.

- VirusScan: een uitgepakte collectie scannen voor virus en/of malware. De actie maakt gebruikt van [een onderliggende service](https://hub.docker.com/r/clamav/clamav). 

- Naming: naamgeving van mappen en bestanden binnen een collectie controleren op bijzonder karakters, ongewenste combinaties en maximale lengte van naamgevingen.

- Sidecar: structuur van een collectie controlleren op de constructie (volgens de sidecar principe), de opbouw van aggregatie niveau's bij ToPX en MDTO, mappen zonder bestanden en de uniciteit van de aangeleverde objecten.

- Profiling
- Exporting
- Reporting
- SignatureUpdate
- Greenlist
- Encoding
- ValidateMetadata
- CreateExcel
- PutSettings
- PreWashMetadata
- IndexMetadataFiles
- DetectPasswordProtection
- UpdateWithPronom
- ValidateBinaries
- 

### Output
### Service
### Status
### Opex
### ToPX naar MDTO

## OpenAPI (Swagger)

## Configuraties

## Docker



