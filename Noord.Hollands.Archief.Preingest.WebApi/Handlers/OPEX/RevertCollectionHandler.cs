using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Logging;
using Noord.Hollands.Archief.Preingest.WebApi.Entities;
using Noord.Hollands.Archief.Preingest.WebApi.Entities.Event;
using Noord.Hollands.Archief.Preingest.WebApi.Entities.Opex;
using Noord.Hollands.Archief.Preingest.WebApi.EventHub;

namespace Noord.Hollands.Archief.Preingest.WebApi.Handlers.OPEX
{
	public class RevertCollectionHandler : AbstractPreingestHandler, IDisposable
    {
		public RevertCollectionHandler(AppSettings settings, IHubContext<PreingestEventHub> eventHub, CollectionHandler preingestCollection)
            : base(settings, eventHub, preingestCollection)
        {
            PreingestEvents += Trigger;
        }
        public void Dispose()
        {
            PreingestEvents -= Trigger;
        }

        public override void Execute()
        {
            var eventModel = CurrentActionProperties(TargetCollection, this.GetType().Name);
            OnTrigger(new PreingestEventArgs { Description = String.Format("Start building Opex for container '{0}'.", TargetCollection), Initiate = DateTimeOffset.Now, ActionType = PreingestActionStates.Started, PreingestAction = eventModel });

            var anyMessages = new List<String>();
            bool isSuccess = false;
            var jsonData = new List<String>();

            try
            {
                if(TargetCollection.EndsWith(".zip", StringComparison.InvariantCultureIgnoreCase))
                {
                    //check if contains opex/pax/xip

                }
                else
                {
                    //check if contains opex folder

                }

                eventModel.ActionData = jsonData.ToArray();
                eventModel.Summary.Processed = jsonData.ToArray().Length;
                eventModel.Summary.Accepted = 0;//totalResult.Count - anyMessages.Count;
                eventModel.Summary.Rejected = anyMessages.Count;
                eventModel.Properties.Messages = anyMessages.ToArray();

                if (eventModel.Summary.Rejected > 0)
                    eventModel.ActionResult.ResultValue = PreingestActionResults.Error;
                else
                    eventModel.ActionResult.ResultValue = PreingestActionResults.Success;

                isSuccess = true;
            }
            catch (Exception e)
            {
                isSuccess = false;
                anyMessages.Clear();
                anyMessages.Add(String.Format("Build Opex with collection: '{0}' failed!", TargetCollection));
                anyMessages.Add(e.Message);
                anyMessages.Add(e.StackTrace);

                Logger.LogError(e, "Build Opex with collection: '{0}' failed!", TargetCollection);

                eventModel.ActionResult.ResultValue = PreingestActionResults.Failed;
                eventModel.Properties.Messages = anyMessages.ToArray();
                eventModel.Summary.Processed = 1;
                eventModel.Summary.Accepted = 0;
                eventModel.Summary.Rejected = 1;

                OnTrigger(new PreingestEventArgs
                {
                    Description = "An exception occured while building opex!",
                    Initiate = DateTimeOffset.Now,
                    ActionType = PreingestActionStates.Failed,
                    PreingestAction = eventModel
                });
            }
            finally
            {
               

                if (isSuccess)
                    OnTrigger(new PreingestEventArgs { Description = "Build Opex with a collection is done.", Initiate = DateTimeOffset.Now, ActionType = PreingestActionStates.Completed, PreingestAction = eventModel });
            }
        }
    }
}

