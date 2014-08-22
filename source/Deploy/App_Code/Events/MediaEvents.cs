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
    public class MediaEvents
    {
        public MediaEvents()
        {
        }

        public void AttachEvents()
        {
            LogHelper.Info(typeof(MediaEvents), "Attaching media events (save, delete, copy, ...)");
            MediaService.Saved += MediaService_Saved;
            MediaService.Deleted += MediaService_Deleted;
            MediaService.EmptyingRecycleBin += MediaService_EmptiedRecycleBin;
            MediaService.Trashed += MediaService_Trashed;
        }

        /// <summary>
        ///  This event is fired when a media is sent to the recycle bin 
        /// </summary>
        void MediaService_Trashed(IMediaService sender, Umbraco.Core.Events.MoveEventArgs<IMedia> e)
        {
            var deployApi = new DeployApiController();

            // For each trashed media
            foreach (var media in e.MoveInfoCollection)
            {
                // Check whether the media has been really trashed 
                if (!MediaHelper.IsMediaTrashed(media.Entity.Id))
                {
                    continue;
                }

                LogHelper.Info<MediaEvents>("Event 'Trashed' -> Trashed Media: {0}", () => media.Entity.Name.ToSafeAlias());

                // For each target site, update the mediaSync's state
                foreach (var targetSite in deployApi.GetTargetSites())
                {
                    var mediaSync = deployApi.GetMediaSync(targetSite.Id, media.Entity.Id, true);
                    mediaSync.SyncState = Models.DatabasePocos.MediaSync.State.modified;
                    deployApi.SaveMediaSync(mediaSync);
                }
            }
        }

        /// <summary>
        ///  This event is fired when the recycle bin is emptied
        /// </summary>
        void MediaService_EmptiedRecycleBin(IMediaService sender, Umbraco.Core.Events.RecycleBinEventArgs e)
        {
            // Only handle medias 
            if (!e.IsMediaRecycleBin)
                return;

            var deployApi = new DeployApiController();

            // For each deleted media
            foreach (var media in ApplicationContext.Current.Services.MediaService.GetByIds(e.Ids))
            {
                LogHelper.Info<MediaEvents>("Event 'Deleted' -> Deleted Media: {0}", () => media.Name.ToSafeAlias());

                // For each target site, update the mediaSync's state to deleted
                foreach (var targetSite in deployApi.GetTargetSites())
                {
                    var mediaSync = deployApi.GetMediaSync(targetSite.Id, media.Id, true);
                    // Mark the item as pendingDelete because it is not possible to check now if it has been really deleted (the media still exists in the DB when the event is triggered)
                    // When the mediaSync items will be retrieved, before displaying them in the UI, the package shall check whether items marked as pendingDeleting 
                    // have been physically deleted
                    mediaSync.SyncState = Models.DatabasePocos.MediaSync.State.pendingDelete;
                    mediaSync.DeletedMediaData = JsonConvert.SerializeObject(media);
                    deployApi.SaveMediaSync(mediaSync);
                }
            }

        }

        /// <summary>
        ///  This event is fired when a media is deleted
        /// </summary>
        void MediaService_Deleted(IMediaService sender, Umbraco.Core.Events.DeleteEventArgs<IMedia> e)
        {
            var deployApi = new DeployApiController();

            // For each deleted media
            foreach (var media in e.DeletedEntities)
            {
                LogHelper.Info<MediaEvents>("Event 'Deleted' -> Deleted Media: {0}", () => media.Name.ToSafeAlias());

                // For each target site, update the mediaSync's state to deleted
                foreach (var targetSite in deployApi.GetTargetSites())
                {
                    var mediaSync = deployApi.GetMediaSync(targetSite.Id, media.Id, true);
                    // Mark the item as pendingDelete because it is not possible to check now if it has been really deleted (the media still exists in the DB when the event is triggered)
                    // When the mediaSync items will be retrieved, before displaying them in the UI, the package shall check whether items marked as pendingDeleting 
                    // have been physically deleted
                    mediaSync.SyncState = Models.DatabasePocos.MediaSync.State.pendingDelete;
                    mediaSync.DeletedMediaData = JsonConvert.SerializeObject(media);
                    deployApi.SaveMediaSync(mediaSync);
                }
            }
        }

        /// <summary>
        ///  This event is fired when a media is created, edited, published, moved, ...
        /// </summary>
        void MediaService_Saved(IMediaService sender, Umbraco.Core.Events.SaveEventArgs<IMedia> e)
        {
            var deployApi = new DeployApiController();

            // For each modified media
            foreach (var media in e.SavedEntities)
            {
                LogHelper.Info<MediaEvents>("Event 'saved' -> Media: {0}", () => media.Name.ToSafeAlias());

                // For each target site, update the mediaSync's state 
                foreach (var targetSite in deployApi.GetTargetSites())
                {
                    var mediaSync = deployApi.GetMediaSync(targetSite.Id, media.Id, true);
                    if (media.IsNewEntity())
                        mediaSync.SyncState = Models.DatabasePocos.MediaSync.State.created;
                    else
                        mediaSync.SyncState = Models.DatabasePocos.MediaSync.State.modified;
                    deployApi.SaveMediaSync(mediaSync);
                }
            }
        }

    }

}
