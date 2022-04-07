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
                    
                    if (this.IsMDTO)
                    {
                        var beperkingenResult = ValidateWithBeperkingenLijstAuteursWet1995(file);
                        schemaResult.IsConfirmBegrippenLijst = beperkingenResult.IsSuccess;
                        List<String> messages = new List<string>();
                        messages.AddRange(schemaResult.ErrorMessages);
                        messages.AddRange(beperkingenResult.Results);
                        schemaResult.ErrorMessages = messages.ToArray();
                    }
                    if (this.IsToPX)
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
                            if(folderMetadataFile == null)
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
                                if(recordOrDossierXml.Root.Element(rdns + "aggregatie").Element(rdns + "aggregatieniveau").Value.Equals("Dossier", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    var openbaarheid = bestandXml.Root.Element(bns + "bestand").Element(bns + "openbaarheid");
                                    if(openbaarheid == null)
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

        private BeperkingResult ValidateWithBeperkingenLijstAuteursWet1995(string file, BeperkingCategorie categorie = BeperkingCategorie.OPENBAARHEID_ARCHIEFWET_1995)
        {
            BeperkingResult validation = new BeperkingResult() { IsSuccess = null, Results = new string[0] { } } ;
            var errorMessages = new List<String>();

            string url = String.Format("http://{0}:{1}/begrippenlijst/{2}", ApplicationSettings.UtilitiesServerName, ApplicationSettings.UtilitiesServerPort, categorie.ToString());
            try
            {
                XDocument xmlDocument = XDocument.Load(file);
                var ns = xmlDocument.Root.GetDefaultNamespace();
                if (xmlDocument.Root.Element(ns + "informatieobject") == null)
                    return validation;//if bestandsType, return immediate

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
                    return validation;//if zero return immediate

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
            catch(Exception e)
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
            return validation;
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
                        sb.Append(String.Format("Element begripLabel met waarde '{0}' niet gevonden in de begrippenlijst", this.BegripLabel));
                    else
                        sb.Append(String.Format("Element begripLabel met waarde '{0}' gevonden in de begrippenlijst", this.BegripLabel));

                    return new BeperkingResult { IsSuccess = (beperkingGebruik != null), Results = new string[] { sb.ToString() } };
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
