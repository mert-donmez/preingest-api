using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;

using Newtonsoft.Json;

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Collections.Generic;

using Noord.Hollands.Archief.Preingest.WebApi.Entities;
using Noord.Hollands.Archief.Preingest.WebApi.EventHub;
using Noord.Hollands.Archief.Preingest.WebApi.Entities.Event;
using Noord.Hollands.Archief.Preingest.WebApi.Entities.Handler;
using System.Threading;
using System.Xml.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Text;
using Noord.Hollands.Archief.Preingest.WebApi.Utilities;
using System.Reflection;
using System.Xml;

namespace Noord.Hollands.Archief.Preingest.WebApi.Handlers
{
    //Check 5
    /// <summary>
    /// Handler for validation all metadata files. It uses XSLWeb for validaiton with XSD + Schematron
    /// </summary>
    /// <seealso cref="Noord.Hollands.Archief.Preingest.WebApi.Handlers.AbstractPreingestHandler" />
    /// <seealso cref="System.IDisposable" />
    public class MetadataValidationHandler : AbstractPreingestHandler, IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MetadataValidationHandler"/> class.
        /// </summary>
        /// <param name="settings">The settings.</param>
        /// <param name="eventHub">The event hub.</param>
        /// <param name="preingestCollection">The preingest collection.</param>
        public MetadataValidationHandler(AppSettings settings, IHubContext<PreingestEventHub> eventHub, CollectionHandler preingestCollection) : base(settings, eventHub, preingestCollection)
        {
            this.PreingestEvents += Trigger;
        }

        /// <summary>
        /// Gets the processing URL.
        /// </summary>
        /// <param name="servername">The servername.</param>
        /// <param name="port">The port.</param>
        /// <param name="pad">The pad.</param>
        /// <returns></returns>
        private String GetProcessingUrl(string servername, string port, string pad)
        {
            string data = this.ApplicationSettings.DataFolderName.EndsWith("/") ? this.ApplicationSettings.DataFolderName : this.ApplicationSettings.DataFolderName + "/";
            string reluri = pad.Remove(0, data.Length);
            string newUri = String.Join("/", reluri.Split("/", StringSplitOptions.None).Select(item => Uri.EscapeDataString(item)));
            return IsToPX ? String.Format(@"http://{0}:{1}/topxvalidation/{2}", servername, port, newUri) : String.Format(@"http://{0}:{1}/mdtovalidation/{2}", servername, port, newUri);
        }

        /// <summary>
        /// Executes this instance.
        /// </summary>
        /// <exception cref="System.Exception">Failed to request data! Status code not equals 200.</exception>
        /// <exception cref="System.ApplicationException">Metadata validation request failed!</exception>
        public override void Execute()
        {
            var eventModel = CurrentActionProperties(TargetCollection, this.GetType().Name);
            OnTrigger(new PreingestEventArgs { Description="Start validate .metadata files.", Initiate = DateTimeOffset.Now, ActionType = PreingestActionStates.Started, PreingestAction = eventModel });

            var anyMessages = new List<String>();
            bool isSucces = false;
            var validation = new List<MetadataValidationItem>();
            try
            {
                base.Execute();

                string sessionFolder = Path.Combine(ApplicationSettings.DataFolderName, SessionGuid.ToString());
                string[] metadatas = IsToPX ? Directory.GetFiles(sessionFolder, "*.metadata", SearchOption.AllDirectories) : Directory.GetFiles(sessionFolder, "*.xml", SearchOption.AllDirectories).Where(item => item.EndsWith(".mdto.xml", StringComparison.InvariantCultureIgnoreCase)).ToArray();

                eventModel.Summary.Processed = metadatas.Count();

                foreach (string file in metadatas)
                {
                    Logger.LogInformation("Metadata validatie : {0}", file);
                    var schemaResult = ValidateWithSchema(file);
                    ValidateNonEmptyStringValues(file, schemaResult);

                    if (this.IsMDTO)
                    {
                        ValidateWithBeperkingenLijstAuteursWet1995(file, schemaResult);
                        ValidateBeperkingGebruikTermijn(file, schemaResult);
                    }
                    if (this.IsToPX)
                    {                        
                        ValidateOpenbaarheidRule(file, schemaResult);                        
                    }

                    validation.Add(schemaResult);

                    OnTrigger(new PreingestEventArgs { Description = String.Format("Processing file '{0}'", file), Initiate = DateTimeOffset.Now, ActionType = PreingestActionStates.Executing, PreingestAction = eventModel });
                }

                eventModel.Summary.Accepted = validation.Where(item => item.IsValidated && item.IsConfirmSchema).Count();
                eventModel.Summary.Rejected = validation.Where(item => !item.IsValidated || !item.IsConfirmSchema).Count();
                eventModel.ActionData = validation.ToArray();

                if (eventModel.Summary.Rejected > 0)
                    eventModel.ActionResult.ResultValue = PreingestActionResults.Error;
                else
                    eventModel.ActionResult.ResultValue = PreingestActionResults.Success;

                isSucces = true;
            }
            catch(Exception e)
            {
                isSucces = false;
                Logger.LogError(e, "An exception occured in metadata validation!");
                anyMessages.Clear();
                anyMessages.Add("An exception occured in metadata validation!");
                anyMessages.Add(e.Message);
                anyMessages.Add(e.StackTrace);

                //eventModel.Summary.Processed = 0;
                eventModel.Summary.Accepted = 0;
                eventModel.Summary.Rejected = eventModel.Summary.Processed;

                eventModel.ActionResult.ResultValue = PreingestActionResults.Failed;
                eventModel.Properties.Messages = anyMessages.ToArray();

                OnTrigger(new PreingestEventArgs { Description= "An exception occured in metadata validation!", Initiate = DateTimeOffset.Now, ActionType = PreingestActionStates.Failed, PreingestAction = eventModel });
            }
            finally
            {
                if (isSucces)
                    OnTrigger(new PreingestEventArgs { Description = "Validation is done!", Initiate = DateTimeOffset.Now, ActionType = PreingestActionStates.Completed, PreingestAction = eventModel });
            }
        }

        /// <summary>
        /// Validates the non empty string values. ToPX and MDTO metadata version
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="schemaResult">The schema result.</param>
        private void ValidateNonEmptyStringValues(string file, MetadataValidationItem schemaResult)
        {
            try
            {
                XmlDocument xml = new XmlDocument();
                xml.Load(file);                

                var nsmgr = new XmlNamespaceManager(xml.NameTable);
                if (this.IsMDTO)
                    nsmgr.AddNamespace("m", "https://www.nationaalarchief.nl/mdto");
                if(this.IsToPX)
                    nsmgr.AddNamespace("t", "http://www.nationaalarchief.nl/ToPX/v2.3");

                XmlNodeList xNodeList = this.IsToPX ? xml.SelectNodes("/t:ToPX//*/text()", nsmgr) : xml.SelectNodes("/m:MDTO//*/text()", nsmgr);
                foreach (XmlNode xNode in xNodeList)
                {
                    string text = xNode.InnerText;
                    string name = xNode.ParentNode.Name;
                    //check if start with non-printable characters - first 128 ASCII characters
                    Match match = Regex.Match(text, @"[^\x20-\x7E]+", RegexOptions.Multiline);
                    if (match.Success)
                    {
                        var findings = schemaResult.ErrorMessages.ToList();
                        findings.Add(String.Format("Non-printable karakter(s) in de tekst gevonden, element: {0} | text: {1}", name, text));
                        schemaResult.ErrorMessages = findings.ToArray();
                    }
                }
            }
            catch (Exception e)
            {
                var errorMessages = new List<String>();
                Logger.LogError(e, String.Format("Exception occured in metadata validation ('ValidateBeperingGebruikTermijn') for metadata file '{0}'!", file));
                errorMessages.Clear();
                errorMessages.Add(String.Format("Exception occured in metadata validation ('ValidateBeperingGebruikTermijn') for metadata file '{0}'!", file));
                errorMessages.Add(e.Message);
                errorMessages.Add(e.StackTrace);

                var currentErrorMessages = schemaResult.ErrorMessages.ToList();
                currentErrorMessages.AddRange(errorMessages);
                //error
                schemaResult.ErrorMessages.ToArray();
            }
        }

        /// <summary>
        /// Validates the bepering gebruik termijn.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="schemaResult">The schema result.</param>
        /// <exception cref="System.ApplicationException"></exception>
        private void ValidateBeperkingGebruikTermijn(string file, MetadataValidationItem schemaResult)
        {
            try
            {
                //parse to xml to see if is valid XML
                XDocument xmlDocument = XDocument.Load(file);
                var ns = xmlDocument.Root.GetDefaultNamespace();
                if (xmlDocument.Root.Element(ns + "informatieobject") == null)
                    return;//if bestandsType, return immediate

                Entities.MDTO.v1_0.mdtoType mdto = DeserializerHelper.DeSerializeObject<Entities.MDTO.v1_0.mdtoType>(File.ReadAllText(file));
                var informatieobject = mdto.Item as Entities.MDTO.v1_0.informatieobjectType;
                if (informatieobject == null)
                    throw new ApplicationException(String.Format("Omzetten naar informatieobject type niet gelukt. Valideren van beperkingGebruikTermijn is niet gelukt voor metadata '{0}'", file));

                if (informatieobject.beperkingGebruik == null)
                    return;

                informatieobject.beperkingGebruik.ToList().ForEach(item =>
                {
                    if (item.beperkingGebruikTermijn == null)
                        return;

                    DateTime? termijnStartdatumLooptijd = item.beperkingGebruikTermijn.termijnStartdatumLooptijd;
                    string termijnLooptijd = item.beperkingGebruikTermijn.termijnLooptijd;
                    string termijnEinddatum = item.beperkingGebruikTermijn.termijnEinddatum;

                    DateTime parseOut = DateTime.MinValue;
                    DateTime.TryParse(termijnEinddatum, out parseOut);

                    DateTime? dtTermijnStartdatumLooptijd = (termijnStartdatumLooptijd.Value == DateTime.MinValue) ? null : termijnStartdatumLooptijd.Value;
                    TimeSpan? tsTermijnLooptijd = String.IsNullOrEmpty(termijnLooptijd) ? null : XmlConvert.ToTimeSpan(termijnLooptijd);
                    DateTime? dtTermijnEinddatum = String.IsNullOrEmpty(termijnEinddatum) ? null : parseOut;

                    if (dtTermijnStartdatumLooptijd.HasValue && tsTermijnLooptijd.HasValue && dtTermijnEinddatum.HasValue)
                    {
                        //calculeren
                        var currentErrorMessages = schemaResult.ErrorMessages.ToList();
                        DateTime isdtTermijnStartdatumLooptijd = dtTermijnStartdatumLooptijd.Value.Add(tsTermijnLooptijd.Value);
                        int result = DateTime.Compare(dtTermijnEinddatum.Value, isdtTermijnStartdatumLooptijd);

                        if (result < 0)
                            currentErrorMessages.Add("Meldig: termijnEinddatum is eerder dan (termijnStartdatumLooptijd + termijnLooptijd)"); //relationship = "is eerder dan";
                        else if (result == 0)
                            currentErrorMessages.Add("Meldig: termijnEinddatum is gelijk (termijnStartdatumLooptijd + termijnLooptijd)");//relationship = "is gelijk aan";
                        else
                            currentErrorMessages.Add("Meldig: termijnEinddatum is later dan (termijnStartdatumLooptijd + termijnLooptijd)");  //relationship = "is later dan";
                        schemaResult.ErrorMessages = currentErrorMessages.ToArray();
                    }
                    if (dtTermijnStartdatumLooptijd.HasValue && !tsTermijnLooptijd.HasValue && dtTermijnEinddatum.HasValue)
                    {
                        /**
                         * 
                         * Check:
                            termijnEinddatum => termijnStartdatum
                            Indien fout: termijnEinddatum is eerder dan termijnStartdatum

                            Melding die ook moet worden gegeven:
                            Er is geen waarde opgegeven voor het element <termijnLooptijd>,  er is wel een <termijnStartdatum>  en <termijnEinddatum>
                         */
                        var currentErrorMessages = schemaResult.ErrorMessages.ToList();
                        int result = DateTime.Compare(dtTermijnStartdatumLooptijd.Value, dtTermijnEinddatum.Value);

                        if (result < 0)
                        {                            
                            //Uitgeschakeld bevinding #4 uit test 10-05-2022 op verzoek van Mark. 
                            //currentErrorMessages.Add("Melding: termijnStartdatumLooptijd is eerder dan termijnEinddatum"); //relationship = "is eerder dan";
                        }
                        else if (result == 0)
                        {
                            currentErrorMessages.Add("Melding: termijnStartdatumLooptijd is gelijk termijnEinddatum");//relationship = "is gelijk aan";
                        }
                        else
                        {
                            currentErrorMessages.Add("Melding: termijnStartdatumLooptijd is later dan termijnEinddatum");  //relationship = "is later dan";
                        }
                         
                        currentErrorMessages.Add("Melding: er is geen waarde opgegeven voor het element 'termijnLooptijd',  er is wel een 'termijnStartdatumLooptijd' en 'termijnEinddatum'");
                        schemaResult.ErrorMessages = currentErrorMessages.ToArray();
                    }
                    if (!dtTermijnStartdatumLooptijd.HasValue && !tsTermijnLooptijd.HasValue && dtTermijnEinddatum.HasValue)
                    {
                        return;
                    }
                    if (!dtTermijnStartdatumLooptijd.HasValue && tsTermijnLooptijd.HasValue && !dtTermijnEinddatum.HasValue)
                    {
                        //Melding: de elementen 'termijnStartdatum en 'termijnEinddatum' ontbreken, er is wel een 'termijnLooptijd'
                        var currentErrorMessages = schemaResult.ErrorMessages.ToList();
                        currentErrorMessages.Add("Melding: de elementen 'termijnStartdatumLooptijd' en 'termijnEinddatum' ontbreken, er is wel een 'termijnLooptijd'");
                        schemaResult.ErrorMessages = currentErrorMessages.ToArray();
                    }
                    if (dtTermijnStartdatumLooptijd.HasValue && tsTermijnLooptijd.HasValue && !dtTermijnEinddatum.HasValue)
                    {
                        //Melding: 'termijnEinddatum' heeft geen waarde, maar 'termijnStartdatum' en 'termijnLooptijd' hebben geldige waarden.
                        var currentErrorMessages = schemaResult.ErrorMessages.ToList();
                        currentErrorMessages.Add("Melding: 'termijnEinddatum' heeft geen waarde, maar 'termijnStartdatumLooptijd' en 'termijnLooptijd' hebben geldige waarden");
                        schemaResult.ErrorMessages = currentErrorMessages.ToArray();
                    }
                    if (!dtTermijnStartdatumLooptijd.HasValue && !tsTermijnLooptijd.HasValue && dtTermijnEinddatum.HasValue)
                    {
                        return;
                    }
                    if (!dtTermijnStartdatumLooptijd.HasValue && !tsTermijnLooptijd.HasValue && !dtTermijnEinddatum.HasValue)
                    {
                        return;
                    }
                });

            }
            catch (Exception e)
            {
                var errorMessages = new List<String>();
                Logger.LogError(e, String.Format("Exception occured in metadata validation ('ValidateBeperingGebruikTermijn') for metadata file '{0}'!", file));
                errorMessages.Clear();
                errorMessages.Add(String.Format("Exception occured in metadata validation ('ValidateBeperingGebruikTermijn') for metadata file '{0}'!", file));
                errorMessages.Add(e.Message);
                errorMessages.Add(e.StackTrace);

                var currentErrorMessages = schemaResult.ErrorMessages.ToList();
                currentErrorMessages.AddRange(errorMessages);
                //error
                schemaResult.ErrorMessages.ToArray();
            }
        }

        /// <summary>
        /// Validates the openbaarheid rule. Only for ToPX.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="schemaResult">The schema result.</param>
        private void ValidateOpenbaarheidRule(string file, MetadataValidationItem schemaResult)
        {
            XDocument bestandXml = XDocument.Load(file);
            XNamespace bns = bestandXml.Root.GetDefaultNamespace();
            if (bestandXml.Root.Elements().First().Name.LocalName == "bestand")
            {
                List<String> messages = new List<string>();
                messages.AddRange(schemaResult.ErrorMessages);

                FileInfo currentMetadataFile = new FileInfo(file);
                DirectoryInfo currentMetadataFolder = currentMetadataFile.Directory;
                String currentMetadataFolderName = currentMetadataFolder.Name;
                var folderMetadataFile = currentMetadataFolder.GetFiles(String.Format("{0}.metadata", currentMetadataFolderName)).FirstOrDefault();
                if (folderMetadataFile == null)
                {
                    messages.AddRange(new string[] {
                                    String.Format("Kan het bovenliggende Dossier of Record metadata bestand met de naam '{0}.metadata' niet vinden in de map '{0}'", currentMetadataFolderName),
                                    String.Format("Controleren op openbaarheid is niet gelukt voor {0}.", file)});
                }
                else
                {
                    //if upper parent metadata is Dossier, need to check openbaarheid in bestand metadata
                    //if pupper parent metadata is Record, just skip
                    XDocument recordOrDossierXml = XDocument.Load(folderMetadataFile.FullName);
                    XNamespace rdns = recordOrDossierXml.Root.GetDefaultNamespace();
                    if (recordOrDossierXml.Root.Element(rdns + "aggregatie").Element(rdns + "aggregatieniveau").Value.Equals("Dossier", StringComparison.InvariantCultureIgnoreCase))
                    {
                        var openbaarheid = bestandXml.Root.Element(bns + "bestand").Element(bns + "openbaarheid");
                        if (openbaarheid == null)
                        {
                            messages.AddRange(new string[] { String.Format("Bovenliggende metadata bestand heeft een aggregatieniveau 'Dossier'. Op bestandsniveau wordt dan 'openbaarheid' element verwacht. Element is niet gevonden") });
                        }
                        else if (openbaarheid.Element(bns + "omschrijvingBeperkingen") == null)
                        {
                            messages.AddRange(new string[] { String.Format("Bovenliggende metadata bestand heeft een aggregatieniveau 'Dossier'. Op bestandsniveau wordt dan 'openbaarheid' element verwacht. Element 'openbaarheid' gevonden maar niet element 'omschrijvingBeperkingen'") });
                        }
                        else
                        {
                            string omschrijvingBeperkingen = openbaarheid.Element(bns + "omschrijvingBeperkingen").Value;
                            Match match = Regex.Match(omschrijvingBeperkingen, "^(Openbaar|Niet openbaar|Beperkt openbaar)$", RegexOptions.ECMAScript);
                            if (!match.Success)
                            {
                                messages.AddRange(new string[] { String.Format("Onjuiste waarde voor element 'omschrijvingBeperkingen' gevonden. Gevonden waarde = '{1}', verwachte waarde = 'Openbaar' of 'Niet openbaar' of 'Beperkt openbaar' in {0}", file, omschrijvingBeperkingen) });
                            }
                        }
                    }
                }

                schemaResult.IsConfirmSchema = !(messages.Count() > 0);
                schemaResult.ErrorMessages = messages.ToArray();
            }
        }

        /// <summary>
        /// Validates metadata with XSD schema. For ToPX and MDTO.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <returns></returns>
        /// <exception cref="System.Exception">Failed to request data! Status code not equals 200.</exception>
        /// <exception cref="System.ApplicationException">Metadata validation request failed!</exception>
        private MetadataValidationItem ValidateWithSchema(string file)
        {
            MetadataValidationItem validation = new MetadataValidationItem();

            string requestUri = GetProcessingUrl(ApplicationSettings.XslWebServerName, ApplicationSettings.XslWebServerPort, file);
            var errorMessages = new List<String>();
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var httpResponse = client.GetAsync(requestUri).Result;

                    if (!httpResponse.IsSuccessStatusCode)
                        throw new Exception("Failed to request data! Status code not equals 200.");

                    var rootError = JsonConvert.DeserializeObject<Root>(httpResponse.Content.ReadAsStringAsync().Result);
                    if (rootError == null)
                        throw new ApplicationException("Metadata validation request failed!");

                    //schema+ validation
                    if (rootError.SchematronValidationReport != null && rootError.SchematronValidationReport.errors != null
                        && rootError.SchematronValidationReport.errors.Count > 0)
                    {
                        var messages = rootError.SchematronValidationReport.errors.Select(item => item.message).ToArray();
                        errorMessages.AddRange(messages);
                    }
                    //default schema validation
                    if (rootError.SchemaValidationReport != null && rootError.SchemaValidationReport.errors != null
                        && rootError.SchemaValidationReport.errors.Count > 0)
                    {
                        var messages = rootError.SchemaValidationReport.errors.Select(item => String.Concat(item.message, ", ", String.Format("Line: {0}, col: {1}", item.line, item.col))).ToArray();
                        errorMessages.AddRange(messages);
                    }

                    if (errorMessages.Count > 0)
                    {
                        //error
                        validation = new MetadataValidationItem
                        {
                            IsValidated = true,
                            IsConfirmSchema = false,
                            IsConfirmBegrippenLijst = null,
                            ErrorMessages = errorMessages.ToArray(),
                            MetadataFilename = file,
                            RequestUri = Uri.UnescapeDataString(requestUri)
                        };
                    }
                    else
                    {
                        //no error
                        validation = new MetadataValidationItem
                        {
                            IsValidated = true,
                            IsConfirmSchema = true,
                            IsConfirmBegrippenLijst = null,
                            ErrorMessages = new string[0],
                            MetadataFilename = file,
                            RequestUri = Uri.UnescapeDataString(requestUri)
                        };
                    }

                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, String.Format("Exception occured in metadata validation with request '{0}' for metadata file '{1}'!", Uri.UnescapeDataString(requestUri), file));
                errorMessages.Clear();
                errorMessages.Add(String.Format("Exception occured in metadata validation with request '{0}' for metadata file '{1}'!", Uri.UnescapeDataString(requestUri), file));
                errorMessages.Add(e.Message);
                errorMessages.Add(e.StackTrace);

                //error
                validation = new MetadataValidationItem
                {
                    IsValidated = false,
                    IsConfirmSchema = false,
                    IsConfirmBegrippenLijst = null,
                    ErrorMessages = errorMessages.ToArray(),
                    MetadataFilename = file,
                    RequestUri = requestUri
                };
            }           
            return validation;
        }

        /// <summary>
        /// Validates the with beperkingen lijst auteurs wet1995. Only for MDTO.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="schemaResult">The schema result.</param>
        private void ValidateWithBeperkingenLijstAuteursWet1995(string file, MetadataValidationItem schemaResult)
        {
            BeperkingCategorie categorie = BeperkingCategorie.OPENBAARHEID_ARCHIEFWET_1995;
            BeperkingResult validation = new BeperkingResult() { IsSuccess = null, Results = new string[0] { } } ;
            var errorMessages = new List<String>();

            string url = String.Format("http://{0}:{1}/begrippenlijst/{2}", ApplicationSettings.UtilitiesServerName, ApplicationSettings.UtilitiesServerPort, categorie.ToString());
            try
            {
                XDocument xmlDocument = XDocument.Load(file);
                var ns = xmlDocument.Root.GetDefaultNamespace();
                if (xmlDocument.Root.Element(ns + "informatieobject") == null)
                {
                    return;//if bestandsType, return immediate
                }

                var countBeperkingGebruik = xmlDocument.Root.Element(ns + "informatieobject").Elements(ns + "beperkingGebruik").Select(item => new Beperking
                {
                    BegripCode = (item.Element(ns + "beperkingGebruikType") == null)
                    ? String.Empty : item.Element(ns + "beperkingGebruikType").Element(ns + "begripCode") == null
                    ? String.Empty : item.Element(ns + "beperkingGebruikType").Element(ns + "begripCode").Value,
                    BegripLabel = (item.Element(ns + "beperkingGebruikType") == null)
                    ? String.Empty : item.Element(ns + "beperkingGebruikType").Element(ns + "begripLabel") == null
                    ? String.Empty : item.Element(ns + "beperkingGebruikType").Element(ns + "begripLabel").Value,
                }).ToList();

                if (countBeperkingGebruik.Count == 0)
                {
                    return;//if zero return immediate
                }

                List<Beperking> beperkingList = new List<Beperking>();
                //load list form MS
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    HttpResponseMessage response = client.GetAsync(url).Result;
                    response.EnsureSuccessStatusCode();
                    var result = JsonConvert.DeserializeObject<Beperking[]>(response.Content.ReadAsStringAsync().Result);
                    beperkingList.AddRange(result);
                }

                //var resultList = countBeperkingGebruik.Select(item => new { Contains = beperkingList.Contains(item), Item = item, Results = item.GetEqualityMessageResults() }).ToList();
                var resultList = countBeperkingGebruik.Select(beperking => beperking.IsItemValid(beperkingList));

                validation = new BeperkingResult
                {
                    IsSuccess = (resultList.Count(item => item.IsSuccess == false) == 0), //no false result means item exists in beperkingLijst
                    Results = resultList.SelectMany(item => item.Results).ToArray(),
                };// String.Format("Controle op element 'beperkingGebruik' is {0}. {1}", item.IsSuccess.HasValue && item.IsSuccess.Value ? "succesvol" : "niet succesvol"
            }
            catch (Exception e)
            {
                Logger.LogError(e, String.Format("Exception occured in metadata validation with request '{0}' for metadata file '{1}'!", url, file));
                errorMessages.Clear();
                errorMessages.Add(String.Format("Exception occured in metadata validation with request '{0}' for metadata file '{1}'!", url, file));
                errorMessages.Add(e.Message);
                errorMessages.Add(e.StackTrace);

                //error
                validation = new BeperkingResult
                {
                    IsSuccess = false,
                    Results = errorMessages.ToArray(),
                };
            }
            finally
            {
                schemaResult.IsConfirmBegrippenLijst = validation.IsSuccess;
                List<String> messages = schemaResult.ErrorMessages.ToList();                
                messages.AddRange(validation.Results);
                schemaResult.ErrorMessages = messages.ToArray();
            }
        }

        internal enum BeperkingCategorie
        {
            INTELLECTUELE_EIGENDOM_CREATIVE_COMMONS_LICENTIES,
            INTELLECTUELE_EIGENDOM_DATABANKWET,
            INTELLECTUELE_EIGENDOM_RIGHTS_STATEMENTS, 
            INTELLECTUELE_EIGENDOM_SOFTWARE_LICENTIES,
            INTELLECTUELE_EIGENDOM_WET_OP_DE_NABURIGE_RECHTEN,
            OPENBAARHEID_ARCHIEFWET_1995,
            OPENBAARHEID_ARCHIEFWET_2021,
            OPENBAARHEID_WET_OPEN_OVERHEID,
            PERSOONSGEGEVENS_AVG,
            TRIGGERS, 
            VOORKEURSFORMATEN
        }

        // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
        internal class Beperking : IEquatable<Beperking>
        { 
            [JsonProperty("begripCode")]
            public string BegripCode { get; set; }

            [JsonProperty("begripLabel")]
            public string BegripLabel { get; set; }

            [JsonProperty("definitie")]
            public string Definitie { get; set; }
           
            public bool Equals(Beperking other)
            {
                bool sameLabel = (this.BegripLabel.Equals(other.BegripLabel, StringComparison.Ordinal));
                bool sameCode = (this.BegripCode.Equals(other.BegripCode, StringComparison.Ordinal));

                return sameCode && sameLabel;
            }

            public BeperkingResult IsItemValid(List<Beperking> beperkingenLijst)
            {
                bool valid = false;
                StringBuilder sb = new StringBuilder(); 
                //if label is empty, result fails immediate
                if (String.IsNullOrEmpty(this.BegripLabel))
                {
                    sb.Append("Element 'begripLabel' is niet voorzien van een waarde.");
                    return new BeperkingResult { IsSuccess = false, Results = new string[] { sb.ToString() } };
                }
                //if label is not empty, but code does, check only label
                if(!String.IsNullOrEmpty(this.BegripLabel) && String.IsNullOrEmpty(this.BegripCode))
                {
                    var beperkingGebruik = beperkingenLijst.FirstOrDefault(item => item.BegripLabel.Equals(this.BegripLabel, StringComparison.Ordinal));
                    if (beperkingGebruik == null)
                    {
                        sb.Append(String.Format("Element begripLabel met waarde '{0}' niet gevonden in de begrippenlijst", this.BegripLabel));
                    }
                    else
                    {
                        sb.Append(String.Format("Element begripLabel met waarde '{0}' gevonden in de begrippenlijst", this.BegripLabel));
                    }
                    sb.Append("Maar element begripCode is niet aanwezig of voorzien van een waarde");
                    return new BeperkingResult { IsSuccess = false, Results = new string[] { sb.ToString() } };
                }
                //check both if not empty
                if (!String.IsNullOrEmpty(this.BegripLabel) && !String.IsNullOrEmpty(this.BegripCode))
                {                    
                    bool contains = beperkingenLijst.Contains(this);
                    if (!contains)
                        sb.Append(String.Format ("Element begripLabel: '{0}' in combinatie met element begripCode '{1}' niet gevonden in de begrippenlijst", this.BegripLabel, this.BegripCode));
                    else
                        sb.Append(String.Format("Gevonden in de begrippenlijst: {0}", this.ToString()));

                    return new BeperkingResult { IsSuccess = contains, Results = new string[] { sb.ToString() } };
                }

                sb.Append(String.Format("Controle niet succesvol uitgevoerd: {0}", this.ToString()));
                return new BeperkingResult { IsSuccess = valid, Results = new string[] { sb.ToString() } }; ;
            }

            public override string ToString()
            {
                return String.Format("begripCode={0}, begripLabel={1}", this.BegripCode, this.BegripLabel);
            }
        }

        internal class BeperkingResult
        {
            public bool? IsSuccess { get; set; }
            public String[] Results { get; set; }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            this.PreingestEvents -= Trigger;
        }
    }
}
