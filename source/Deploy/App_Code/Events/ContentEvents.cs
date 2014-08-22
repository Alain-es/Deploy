using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Umbraco.Core;
using Umbraco.Core.Services;
using Umbraco.Core.Models;
using Umbraco.Core.Logging;
using Umbraco.Web;

using Newtonsoft.Json;

using Deploy.Helpers;
using Deploy.Controllers.Api;
using Deploy.Models.DatabasePocos;

namespace Deploy.Installer
{
    public class ContentEvents
    {
        public ContentEvents()
        {
        }

        public void AttachEvents()
        {
            LogHelper.Info(typeof(ContentEvents), "Attaching content events (save, delete, copy, ...)");
            ContentService.Saved += ContentService_Saved;
            ContentService.Deleted += ContentService_Deleted;
            ContentService.Copied += ContentService_Copied;
            ContentService.EmptyingRecycleBin += ContentService_EmptiedRecycleBin;
            ContentService.Trashed += ContentService_Trashed;
        }

        /// <summary>
        ///  This event is fired when a content is sent to the recycle bin 
        /// </summary>
        void ContentService_Trashed(IContentService sender, Umbraco.Core.Events.MoveEventArgs<IContent> e)
        {
            var deployApi = new DeployApiController();

            // For each trashed content
            foreach (var content in e.MoveInfoCollection)
            {
                // Check whether the content has been really trashed 
                if (!ContentHelper.IsContentTrashed(content.Entity.Id))
                {
                    continue;
                }

                LogHelper.Info<ContentEvents>("Event 'Trashed' -> Trashed Content: {0}", () => content.Entity.Name.ToSafeAlias());

                // For each target site, update the contentSync's state
                foreach (var targetSite in deployApi.GetTargetSites())
                {
                    var contentSync = deployApi.GetContentSync(targetSite.Id, content.Entity.Id, true);
                    contentSync.SyncState = Models.DatabasePocos.ContentSync.State.modified;
                    deployApi.SaveContentSync(contentSync);
                }
            }
        }

        /// <summary>
        ///  This event is fired when the recycle bin is emptied
        /// </summary>
        void ContentService_EmptiedRecycleBin(IContentService sender, Umbraco.Core.Events.RecycleBinEventArgs e)
        {
            // Only handle contents 
            if (!e.IsContentRecycleBin)
                return;

            var deployApi = new DeployApiController();

            // For each deleted content
            foreach (var content in ApplicationContext.Current.Services.ContentService.GetByIds(e.Ids))
            {
                LogHelper.Info<ContentEvents>("Event 'Deleted' -> Deleted Content: {0}", () => content.Name.ToSafeAlias());

                // For each target site, update the contentSync's state to deleted
                foreach (var targetSite in deployApi.GetTargetSites())
                {
                    var contentSync = deployApi.GetContentSync(targetSite.Id, content.Id, true);
                    // Mark the item as pendingDelete because it is not possible to check now if it has been really deleted (the content still exists in the DB when the event is triggered)
                    // When the contentSync items will be retrieved, before displaying them in the UI, the package shall check whether items marked as pendingDeleting 
                    // have been physically deleted
                    contentSync.SyncState = Models.DatabasePocos.ContentSync.State.pendingDelete;
                    contentSync.DeletedContentData = JsonConvert.SerializeObject(content);
                    deployApi.SaveContentSync(contentSync);
                }
            }

        }

        /// <summary>
        ///  This event is fired when a content is copied
        /// </summary>
        void ContentService_Copied(IContentService sender, Umbraco.Core.Events.CopyEventArgs<IContent> e)
        {
            var deployApi = new DeployApiController();

            LogHelper.Info<ContentEvents>("Event 'copied' -> New Content: {0}", () => e.Copy.Name.ToSafeAlias());

            // For each target site, we create a new CotentSync item
            foreach (var targetSite in deployApi.GetTargetSites())
            {
                var contentSync = deployApi.GetContentSync(targetSite.Id, e.Copy.Id, true);
                contentSync.SyncState = Models.DatabasePocos.ContentSync.State.created;
                deployApi.SaveContentSync(contentSync);
            }
        }

        /// <summary>
        ///  This event is fired when a content is deleted
        /// </summary>
        void ContentService_Deleted(IContentService sender, Umbraco.Core.Events.DeleteEventArgs<IContent> e)
        {
            var deployApi = new DeployApiController();

            // For each deleted content
            foreach (var content in e.DeletedEntities)
            {
                LogHelper.Info<ContentEvents>("Event 'Deleted' -> Deleted Content: {0}", () => content.Name.ToSafeAlias());

                // For each target site, update the contentSync's state to deleted
                foreach (var targetSite in deployApi.GetTargetSites())
                {
                    var contentSync = deployApi.GetContentSync(targetSite.Id, content.Id, true);
                    // Mark the item as pendingDelete because it is not possible to check now if it has been really deleted (the content still exists in the DB when the event is triggered)
                    // When the contentSync items will be retrieved, before displaying them in the UI, the package shall check whether items marked as pendingDeleting 
                    // have been physically deleted
                    contentSync.SyncState = Models.DatabasePocos.ContentSync.State.pendingDelete;
                    contentSync.DeletedContentData = JsonConvert.SerializeObject(content);
                    deployApi.SaveContentSync(contentSync);
                }
            }
        }

        /// <summary>
        ///  This event is fired when a content is created, edited, published, moved, ...
        /// </summary>
        void ContentService_Saved(IContentService sender, Umbraco.Core.Events.SaveEventArgs<IContent> e)
        {
            var deployApi = new DeployApiController();

            // For each modified content
            foreach (var content in e.SavedEntities)
            {
                LogHelper.Info<ContentEvents>("Event 'saved' -> Content: {0}", () => content.Name.ToSafeAlias());

                // For each target site, update the contentSync's state 
                foreach (var targetSite in deployApi.GetTargetSites())
                {
                    var contentSync = deployApi.GetContentSync(targetSite.Id, content.Id, true);
                    if (content.IsNewEntity())
                        contentSync.SyncState = Models.DatabasePocos.ContentSync.State.created;
                    else
                        contentSync.SyncState = Models.DatabasePocos.ContentSync.State.modified;
                    deployApi.SaveContentSync(contentSync);
                }
            }
        }

    }
}
