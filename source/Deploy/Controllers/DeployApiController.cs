using System;
using System.Web;
using System.Web.Mvc;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Dynamic;
using System.ComponentModel;
using System.Text;

using umbraco;
using Umbraco.Core;
using Umbraco.Core.Dynamics;
using Umbraco.Core.Models;
using Umbraco.Core.Logging;
using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.DatabaseAnnotations;
using Umbraco.Core.Persistence.DatabaseModelDefinitions;
using Umbraco.Core.IO;
using umbraco.BusinessLogic.Actions;
using umbraco.BusinessLogic;
using Umbraco.Web;
using Umbraco.Web.Editors;
using Umbraco.Web.Mvc;
using Umbraco.Web.WebApi;
using Umbraco.Web.Models.ContentEditing;

using AutoMapper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Deploy.Models;
using Deploy.Models.DatabasePocos;
using Deploy.Helpers;

namespace Deploy.Controllers.Api
{
    [PluginController("Deploy")]
    [IsBackOffice]
    public class DeployApiController : UmbracoAuthorizedJsonController
    {

        #region ContentSync

        /// <summary>
        /// Retrieve all ContentSyncs for a given TargetSite
        /// </summary>
        /// <param name="targetSiteId"></param>
        /// <param name="CheckUserPermissions">Check whether the logged in user got browsing rights on contents. If true then it returns only contents for which the user has sufficient rights. </param>
        /// <returns></returns>
        [HttpGet]
        public IEnumerable<ContentSync> GetAllContentSyncsByTargetSite(int targetSiteId, bool CheckUserPermissions)
        {
            // Before retrieving data it is necessary to check contentSyncs marked as pendingDelete
            persistPendingDeleteContentSyncs();

            // Now we can retrieve the data
            var db = ApplicationContext.DatabaseContext.Database;
            var sql = Sql.Builder
                .Select("*")
                .From<ContentSync>()
                .Where<ContentSync>(x => x.TargetSiteId == targetSiteId);
            var contentSyncs = db.Fetch<ContentSync>(sql).ToList<ContentSync>();
            if (!CheckUserPermissions)
                return contentSyncs;

            // Check whether the logged in user got browsing rights on contents
            var result = new List<ContentSync>();
            foreach (var contentSync in contentSyncs)
            {
                // If it is a deleted item, it is not possible to check permissions
                // As a work around, only users who have enough rights to access the recycle bin will see deleted items
                if (contentSync.SyncState == ContentSync.State.deleted)
                {
                    //var content = PermissionHelper.CheckContentPermissions(Services.UserService, , Services.ContentService, Constants.System.RecycleBinContent, new char[] { ActionBrowse.Instance.Letter });
                    var hasCurrentUserAccessToRecycleBin = UserExtensions.HasPathAccess(
                                                          Constants.System.RecycleBinContent.ToInvariantString(),
                                                          Security.CurrentUser.StartContentId,
                                                          Constants.System.RecycleBinContent);
                    if (hasCurrentUserAccessToRecycleBin)
                        result.Add(contentSync);
                }
                else
                {
                    var content = PermissionHelper.CheckContentPermissions(Services.UserService, Security.CurrentUser, Services.ContentService, contentSync.ContentId, new char[] { ActionBrowse.Instance.Letter });
                    if (content != null)
                        result.Add(contentSync);
                }
            }
            return result;
        }

        /// <summary>
        /// Retrieve all Contents with pending changes (syncState = 0, 2 ,3, 4) for a given TargetSite 
        /// </summary>
        /// <param name="targetSiteId"></param>
        /// <param name="CheckUserPermissions">Check whether the logged in user got browsing rights on contents. If true then it returns only contents for which the user has sufficient rights. </param>
        /// <returns></returns>
        [HttpGet]
        public IEnumerable<IContent> GetContentsWithPendingChangesByTargetSite(int targetSiteId, bool CheckUserPermissions)
        {
            var result = new List<IContent>();
            foreach (var contentSync in GetAllContentSyncsByTargetSite(targetSiteId, CheckUserPermissions))
            {
                switch (contentSync.SyncState)
                {

                    case ContentSync.State.unknown:
                    case ContentSync.State.created:
                    case ContentSync.State.modified:
                        var content = Services.ContentService.GetById(contentSync.ContentId);
                        if (content != null)
                            result.Add(content);
                        break;

                    case ContentSync.State.deleted:
                        // Because the content has been deleted, we extract the data stored in the contentSync entity to create a temporary Content entity
                        // in order to display the deleted node in the listview
                        if (contentSync.DeletedContentData != null)
                        {
                            dynamic jsonContentDeleted = null;
                            try
                            {
                                jsonContentDeleted = JObject.Parse(contentSync.DeletedContentData);
                            }
                            catch (Exception) { }
                            if (jsonContentDeleted != null)
                            {
                                // Check whether the content type exists (when a content type is deleted from the Umbraco UI, all contents for this CT are deleted)
                                IContentType contentType = null;
                                contentType = Services.ContentTypeService.GetContentType((int)jsonContentDeleted.ContentTypeId);
                                if (contentType != null)
                                {
                                    var contentDeleted = new Content(jsonContentDeleted.Name.ToString(), (int)jsonContentDeleted.ParentId, contentType);
                                    if (contentDeleted != null)
                                    {
                                        contentDeleted.Id = jsonContentDeleted.Id;
                                        contentDeleted.UpdateDate = jsonContentDeleted.UpdateDate;
                                        contentDeleted.WriterId = jsonContentDeleted.WriterId;
                                        // --------------------- TODO: add the level to the List we return in order to sort from angularJS by level
                                        //contentDeleted.Level = 1;
                                        result.Add(contentDeleted);
                                    }
                                }
                            }
                        }
                        break;

                    case ContentSync.State.synced:
                    case ContentSync.State.pendingDelete:
                    default:
                        // Nothing to do
                        break;
                }
            }
            return result;
        }

        [HttpGet]
        public PagedResult<ContentItemBasic<ContentPropertyBasic, IContent>> GetPagedContentsWithPendingChangesByTargetSite(
            int targetSiteId,
            int pageNumber = 0,
            int pageSize = 0,
            string orderBy = "SortOrder",
            Direction orderDirection = Direction.Ascending,
            string filter = "")
        {
            return GetPagedContents(GetContentsWithPendingChangesByTargetSite(targetSiteId, true), pageNumber, pageSize, orderBy, orderDirection, filter);
        }

        private PagedResult<ContentItemBasic<ContentPropertyBasic, IContent>> GetPagedContents(
            IEnumerable<IContent> contentNodes,
            int pageNumber = 0,
            int pageSize = 0,
            string orderBy = "SortOrder",
            Direction orderDirection = Direction.Ascending,
            string filter = "")
        {

            var children = contentNodes.ToArray();
            var totalChildren = children.Length;

            if (totalChildren == 0)
                return new PagedResult<ContentItemBasic<ContentPropertyBasic, IContent>>(0, 0, 0);

            var result = children
                .Select(Mapper.Map<IContent, ContentItemBasic<ContentPropertyBasic, IContent>>)
                .AsQueryable();

            if (!string.IsNullOrEmpty(filter))
            {
                filter = filter.ToLower();
                result = result.Where(x => x.Name.InvariantContains(filter));
            }

            var orderedResult = orderDirection == Direction.Ascending
                ? result.OrderBy(orderBy)
                : result.OrderByDescending(orderBy);

            var pagedResult = new PagedResult<ContentItemBasic<ContentPropertyBasic, IContent>>(
               totalChildren,
               pageNumber,
               pageSize);

            if (pageNumber > 0 && pageSize > 0)
            {
                var skipSize = Convert.ToInt32((pageNumber - 1) * pageSize);
                pagedResult.Items = orderedResult
                    .Skip(skipSize)
                    .Take(pageSize);
            }
            else
            {
                pagedResult.Items = orderedResult;
            }

            return pagedResult;
        }

        [HttpGet]
        public IEnumerable<ContentSync> GetContentSyncsByContentId(int contentId)
        {
            // Before retrieving data it is necessary to check contentSyncs marked as pendingDelete
            persistPendingDeleteContentSyncs();

            // Now we can retrieve the data
            IEnumerable<ContentSync> result;
            var db = ApplicationContext.DatabaseContext.Database;
            var sql = Sql.Builder
                .Select("*")
                .From<ContentSync>()
                .Where<ContentSync>(x => x.ContentId == contentId);
            result = db.Fetch<ContentSync>(sql);
            return result;
        }

        [HttpGet]
        public ContentSync GetContentSync(int targetSiteId, int contentId)
        {
            // Before retrieving data it is necessary to check contentSyncs marked as pendingDelete
            persistPendingDeleteContentSyncs();

            // Now we can retrieve the data
            ContentSync result = null;
            var db = ApplicationContext.DatabaseContext.Database;
            var sql = Sql.Builder
                .Select("*")
                .From<ContentSync>()
                .Where<ContentSync>(x => x.TargetSiteId == targetSiteId && x.ContentId == contentId);
            result = db.Fetch<ContentSync>(sql).FirstOrDefault();
            return result;
        }

        [HttpGet]
        public ContentSync GetContentSync(int targetSiteId, int contentId, bool createIfNotFound)
        {
            ContentSync result = GetContentSync(targetSiteId, contentId);
            if (result == null && createIfNotFound)
            {
                result = new ContentSync() { TargetSiteId = targetSiteId, ContentId = contentId, SyncState = ContentSync.State.unknown };
                SaveContentSync(result);
            }
            return result;
        }

        [HttpGet]
        public IEnumerable<ContentSync> GetContentSyncAndParentSync(int targetSiteId, int contentId)
        {
            var result = new List<ContentSync>();

            // Content
            var contentSync = GetContentSync(targetSiteId, contentId);
            if (contentSync != null)
            {
                // If it is a deleted content, we don't need to return the parent node
                if (contentSync.SyncState == ContentSync.State.deleted)
                {
                    // Content (only)
                    result.Add(contentSync);
                }
                else
                {
                    // Get Content's parent
                    var content = Services.ContentService.GetById(contentSync.ContentId);
                    if (content != null)
                    {
                        // If the parent node is root or recycle bin, we don't need to return the parent node
                        if (content.ParentId == Constants.System.Root || content.ParentId == Constants.System.RecycleBinContent)
                        {
                            // Content (only)
                            result.Add(contentSync);
                        }
                        else
                        {
                            // Check whether there is a contenSync for the parent node and the mapping Id is not empty
                            var parentContentSync = GetContentSync(targetSiteId, content.ParentId);
                            if (parentContentSync != null && parentContentSync.TargetContentGuid != Guid.Empty)
                            {
                                // Content
                                result.Add(contentSync);
                                // Content's parent 
                                result.Add(parentContentSync);
                            }
                        }
                    }
                }
            }
            return result;
        }

        [HttpPost]
        public void SaveContentSync(ContentSync contentSync)
        {
            // Permitted SyncState transitions
            // For instance, if a user creates a new content, the state will be set to "new". If the same content is modified but has not been 
            // deployed yet, we need to keep the state "new" and not modify it with "modified"

            // If true, the contentSync item will be deleted instead of being saved
            bool deleteContenSync = false;

            // Get the content sync current value
            var contentSyncCurrentValue = GetContentSync(contentSync.TargetSiteId, contentSync.ContentId);
            if (contentSyncCurrentValue != null)
            {
                var syncStateCurrentValue = contentSyncCurrentValue.SyncState;
                var warningMessage = false;

                switch (syncStateCurrentValue)
                {
                    case ContentSync.State.unknown:
                        switch (contentSync.SyncState)
                        {
                            case ContentSync.State.modified:
                            case ContentSync.State.deleted:
                            case ContentSync.State.synced:
                            case ContentSync.State.created:
                            case ContentSync.State.pendingDelete:
                                // Ok
                                break;
                            case ContentSync.State.unknown:
                            default:
                                // This sync state transition should not happen!
                                warningMessage = true;
                                // Keep the current sync state
                                contentSync.SyncState = ContentSync.State.unknown;
                                break;
                        }
                        break;

                    case ContentSync.State.created:
                        switch (contentSync.SyncState)
                        {
                            case ContentSync.State.synced:
                                // Ok
                                break;
                            case ContentSync.State.modified:
                                // The content has not yet been synchronized , so must be kept as it is
                                contentSync.SyncState = ContentSync.State.created;
                                break;
                            case ContentSync.State.pendingDelete:
                            case ContentSync.State.deleted:
                                // The content has not yet been synchronized, so must never be created or synchronized
                                deleteContenSync = true;
                                break;
                            case ContentSync.State.unknown:
                            case ContentSync.State.created:
                            default:
                                // This sync state transition should not happen!
                                warningMessage = true;
                                // Keep the current sync state
                                contentSync.SyncState = ContentSync.State.created;
                                break;
                        }
                        break;

                    case ContentSync.State.modified:
                        switch (contentSync.SyncState)
                        {
                            case ContentSync.State.synced:
                            case ContentSync.State.deleted:
                            case ContentSync.State.modified:
                            case ContentSync.State.pendingDelete:
                                // Ok
                                break;
                            case ContentSync.State.unknown:
                            case ContentSync.State.created:
                            default:
                                // This sync state transition should not happen!
                                warningMessage = true;
                                // Keep the current sync state
                                contentSync.SyncState = ContentSync.State.modified;
                                break;
                        }
                        break;

                    case ContentSync.State.pendingDelete:
                        switch (contentSync.SyncState)
                        {
                            case ContentSync.State.deleted:
                                // Ok
                                break;
                            case ContentSync.State.synced:
                            case ContentSync.State.modified:
                                // This sync state transition should not happen!
                                // But allow the state transition in order to allow to restoring wrong deletings
                                warningMessage = true;
                                break;
                            case ContentSync.State.unknown:
                            case ContentSync.State.created:
                            case ContentSync.State.pendingDelete:
                            default:
                                // This sync state transition should not happen!
                                warningMessage = true;
                                // Keep the current sync state
                                contentSync.SyncState = ContentSync.State.pendingDelete;
                                break;
                        }
                        break;

                    case ContentSync.State.deleted:
                        switch (contentSync.SyncState)
                        {
                            case ContentSync.State.synced:
                                // The content has been updated in the target site, so delete the contentSync since it is not necessary anymore
                                deleteContenSync = true;
                                break;
                            case ContentSync.State.unknown:
                            case ContentSync.State.created:
                            case ContentSync.State.modified:
                            case ContentSync.State.deleted:
                            case ContentSync.State.pendingDelete:
                            default:
                                // This sync state transition should not happen!
                                warningMessage = true;
                                // Keep the current sync state
                                contentSync.SyncState = ContentSync.State.deleted;
                                break;
                        }
                        break;

                    case ContentSync.State.synced:
                        switch (contentSync.SyncState)
                        {
                            case ContentSync.State.modified:
                            case ContentSync.State.deleted:
                            case ContentSync.State.synced:
                            case ContentSync.State.pendingDelete:
                                // Ok
                                break;
                            case ContentSync.State.unknown:
                            case ContentSync.State.created:
                            default:
                                // This sync state transition should not happen!
                                warningMessage = true;
                                // Keep the current sync state
                                contentSync.SyncState = ContentSync.State.synced;
                                break;
                        }
                        break;

                    default:
                        // This sync state transition should not happen!
                        warningMessage = true;
                        break;
                }

                if (warningMessage)
                    LogHelper.Warn<DeployApiController>("Content id = {0}, the sync state transition '{1}->{2}' should not happen! ", () => contentSync.ContentId, () => syncStateCurrentValue.ToString(), () => contentSync.SyncState.ToString());

            }

            // Save/Delete item
            var db = ApplicationContext.DatabaseContext.Database;
            if (deleteContenSync)
            {
                db.Delete(contentSync);
            }
            else
            {
                db.Save(contentSync);
            }

        }

        [HttpPost]
        public bool DeleteContentSync(ContentSync contentSync)
        {
            var result = true;
            var db = ApplicationContext.DatabaseContext.Database;
            if (db.Delete(contentSync) != 1)
                result = false;
            return result;
        }

        /// <summary>
        /// Retrieve all contentSync items from the database which have the state 'pendingDelete' and check whether their associated contents have been physically deleted from the database
        /// This two step contentSync delete process is necesssary because the delete event is triggered before the content is really deleted, so it is not possible when the event is triggered if the
        /// content has been deleted. 
        /// We don't trust the delete event since once it was triggered for ALL contents available in the database (loosing all the sync info)!! And because it could happen again, we want to be protected
        /// </summary>
        private void persistPendingDeleteContentSyncs()
        {
            var db = ApplicationContext.DatabaseContext.Database;
            var sql = Sql.Builder
                .Select("*")
                .From<ContentSync>()
                .Where<ContentSync>(x => x._SyncState == (int)ContentSync.State.pendingDelete);
            var contentSyncs = db.Fetch<ContentSync>(sql).ToList<ContentSync>();
            foreach (var contentSync in contentSyncs)
            {
                // Mark the contentSync as deleted
                if (ContentHelper.IsContentDeleted(contentSync.ContentId))
                {
                    contentSync.SyncState = ContentSync.State.deleted;
                    // The method SaveContentSync() must not used in order to avoid an infinite loop
                    db.Save(contentSync);
                }
            }
        }

        #endregion

        #region MediaSync

        /// <summary>
        /// Retrieve all MediaSyncs for a given TargetSite
        /// </summary>
        /// <param name="targetSiteId"></param>
        /// <param name="CheckUserPermissions">Check whether the logged in user got browsing rights on medias. If true then it returns only medias for which the user has sufficient rights. </param>
        /// <returns></returns>
        [HttpGet]
        public IEnumerable<MediaSync> GetAllMediaSyncsByTargetSite(int targetSiteId, bool CheckUserPermissions)
        {
            // Before retrieving data it is necessary to check mediaSyncs marked as pendingDelete
            persistPendingDeleteMediaSyncs();

            // Now we can retrieve the data
            var db = ApplicationContext.DatabaseContext.Database;
            var sql = Sql.Builder
                .Select("*")
                .From<MediaSync>()
                .Where<MediaSync>(x => x.TargetSiteId == targetSiteId);
            var mediaSyncs = db.Fetch<MediaSync>(sql).ToList<MediaSync>();
            if (!CheckUserPermissions)
                return mediaSyncs;

            // Check whether the logged in user got browsing rights on medias
            var result = new List<MediaSync>();
            foreach (var mediaSync in mediaSyncs)
            {
                // If it is a deleted item, it is not possible to check permissions
                // As a work around, only users who have enough rights to access the recycle bin will see deleted items
                if (mediaSync.SyncState == MediaSync.State.deleted)
                {
                    //var media = PermissionHelper.CheckMediaPermissions(Services.UserService, , Services.MediaService, Constants.System.RecycleBinMedia, new char[] { ActionBrowse.Instance.Letter });
                    var hasCurrentUserAccessToRecycleBin = UserExtensions.HasPathAccess(
                                                          Constants.System.RecycleBinMedia.ToInvariantString(),
                                                          Security.CurrentUser.StartMediaId,
                                                          Constants.System.RecycleBinMedia);
                    if (hasCurrentUserAccessToRecycleBin)
                        result.Add(mediaSync);
                }
                else
                {
                    var media = PermissionHelper.CheckMediaPermissions(Services.UserService, Security.CurrentUser, Services.MediaService, mediaSync.MediaId, new char[] { ActionBrowse.Instance.Letter });
                    if (media != null)
                        result.Add(mediaSync);
                }
            }
            return result;
        }

        /// <summary>
        /// Retrieve all Medias with pending changes (syncState = 0, 2 ,3, 4) for a given TargetSite 
        /// </summary>
        /// <param name="targetSiteId"></param>
        /// <param name="CheckUserPermissions">Check whether the logged in user got browsing rights on medias. If true then it returns only medias for which the user has sufficient rights. </param>
        /// <returns></returns>
        [HttpGet]
        public IEnumerable<IMedia> GetMediasWithPendingChangesByTargetSite(int targetSiteId, bool CheckUserPermissions)
        {
            var result = new List<IMedia>();
            foreach (var mediaSync in GetAllMediaSyncsByTargetSite(targetSiteId, CheckUserPermissions))
            {
                switch (mediaSync.SyncState)
                {

                    case MediaSync.State.unknown:
                    case MediaSync.State.created:
                    case MediaSync.State.modified:
                        var media = Services.MediaService.GetById(mediaSync.MediaId);
                        if (media != null)
                            result.Add(media);
                        break;

                    case MediaSync.State.deleted:
                        // Because the media has been deleted, we extract the data stored in the mediaSync entity to create a temporary Media entity
                        // in order to display the deleted node in the listview
                        if (mediaSync.DeletedMediaData != null)
                        {
                            dynamic jsonMediaDeleted = null;
                            try
                            {
                                jsonMediaDeleted = JObject.Parse(mediaSync.DeletedMediaData);
                            }
                            catch (Exception) { }
                            if (jsonMediaDeleted != null)
                            {
                                // Check whether the media type exists  (when a media type is deleted from the Umbraco UI, all medias for this CT are deleted)
                                IMediaType mediaType = null;
                                mediaType = Services.ContentTypeService.GetMediaType((int)jsonMediaDeleted.ContentTypeId);
                                if (mediaType != null)
                                {
                                    var mediaDeleted = new Media(jsonMediaDeleted.Name.ToString(), (int)jsonMediaDeleted.ParentId, mediaType);
                                    if (mediaDeleted != null)
                                    {
                                        mediaDeleted.Id = jsonMediaDeleted.Id;
                                        mediaDeleted.UpdateDate = jsonMediaDeleted.UpdateDate;
                                        //mediaDeleted.WriterId = jsonMediaDeleted.WriterId;
                                        result.Add(mediaDeleted);
                                    }
                                }
                            }
                        }
                        break;

                    case MediaSync.State.synced:
                    case MediaSync.State.pendingDelete:
                    default:
                        // Nothing to do
                        break;
                }
            }
            return result;
        }

        [HttpGet]
        public PagedResult<ContentItemBasic<ContentPropertyBasic, IMedia>> GetPagedMediasWithPendingChangesByTargetSite(
            int targetSiteId,
            int pageNumber = 0,
            int pageSize = 0,
            string orderBy = "SortOrder",
            Direction orderDirection = Direction.Ascending,
            string filter = "")
        {
            return GetPagedMedias(GetMediasWithPendingChangesByTargetSite(targetSiteId, true), pageNumber, pageSize, orderBy, orderDirection, filter);
        }

        private PagedResult<ContentItemBasic<ContentPropertyBasic, IMedia>> GetPagedMedias(
            IEnumerable<IMedia> mediaNodes,
            int pageNumber = 0,
            int pageSize = 0,
            string orderBy = "SortOrder",
            Direction orderDirection = Direction.Ascending,
            string filter = "")
        {

            var children = mediaNodes.ToArray();
            var totalChildren = children.Length;

            if (totalChildren == 0)
                return new PagedResult<ContentItemBasic<ContentPropertyBasic, IMedia>>(0, 0, 0);

            var result = children
                .Select(Mapper.Map<IMedia, ContentItemBasic<ContentPropertyBasic, IMedia>>)
                .AsQueryable();

            if (!string.IsNullOrEmpty(filter))
            {
                filter = filter.ToLower();
                result = result.Where(x => x.Name.InvariantContains(filter));
            }

            var orderedResult = orderDirection == Direction.Ascending
                ? result.OrderBy(orderBy)
                : result.OrderByDescending(orderBy);

            var pagedResult = new PagedResult<ContentItemBasic<ContentPropertyBasic, IMedia>>(
               totalChildren,
               pageNumber,
               pageSize);

            if (pageNumber > 0 && pageSize > 0)
            {
                var skipSize = Convert.ToInt32((pageNumber - 1) * pageSize);
                pagedResult.Items = orderedResult
                    .Skip(skipSize)
                    .Take(pageSize);
            }
            else
            {
                pagedResult.Items = orderedResult;
            }

            return pagedResult;
        }

        [HttpGet]
        public IEnumerable<MediaSync> GetMediaSyncsByMediaId(int mediaId)
        {
            // Before retrieving data it is necessary to check mediaSyncs marked as pendingDelete
            persistPendingDeleteMediaSyncs();

            // Now we can retrieve the data
            IEnumerable<MediaSync> result;
            var db = ApplicationContext.DatabaseContext.Database;
            var sql = Sql.Builder
                .Select("*")
                .From<MediaSync>()
                .Where<MediaSync>(x => x.MediaId == mediaId);
            result = db.Fetch<MediaSync>(sql);
            return result;
        }

        [HttpGet]
        public MediaSync GetMediaSync(int targetSiteId, int mediaId)
        {
            // Before retrieving data it is necessary to check mediaSyncs marked as pendingDelete
            persistPendingDeleteMediaSyncs();

            // Now we can retrieve the data
            MediaSync result = null;
            var db = ApplicationContext.DatabaseContext.Database;
            var sql = Sql.Builder
                .Select("*")
                .From<MediaSync>()
                .Where<MediaSync>(x => x.TargetSiteId == targetSiteId && x.MediaId == mediaId);
            result = db.Fetch<MediaSync>(sql).FirstOrDefault();
            return result;
        }

        [HttpGet]
        public MediaSync GetMediaSync(int targetSiteId, int mediaId, bool createIfNotFound)
        {
            MediaSync result = GetMediaSync(targetSiteId, mediaId);
            if (result == null && createIfNotFound)
            {
                result = new MediaSync() { TargetSiteId = targetSiteId, MediaId = mediaId, SyncState = MediaSync.State.unknown };
                SaveMediaSync(result);
            }
            return result;
        }


        [HttpGet]
        public IEnumerable<MediaSync> GetMediaSyncAndParentSync(int targetSiteId, int mediaId)
        {
            var result = new List<MediaSync>();

            // Media
            var mediaSync = GetMediaSync(targetSiteId, mediaId);
            if (mediaSync != null)
            {
                // If it is a deleted media, we don't need to return the parent node
                if (mediaSync.SyncState == MediaSync.State.deleted)
                {
                    // Media (only)
                    result.Add(mediaSync);
                }
                else
                {
                    // Get Media's parent
                    var media = Services.MediaService.GetById(mediaSync.MediaId);
                    if (media != null)
                    {
                        // If the parent node is root or recycle bin, we don't need to return the parent node
                        if (media.ParentId == Constants.System.Root || media.ParentId == Constants.System.RecycleBinMedia)
                        {
                            // Media (only)
                            result.Add(mediaSync);
                        }
                        else
                        {
                            // Check whether there is a contenSync for the parent node and the mapping Id is not empty
                            var parentMediaSync = GetMediaSync(targetSiteId, media.ParentId);
                            if (parentMediaSync != null && parentMediaSync.TargetMediaGuid != Guid.Empty)
                            {
                                // Media
                                result.Add(mediaSync);
                                // Media's parent 
                                result.Add(parentMediaSync);
                            }
                        }
                    }
                }
            }
            return result;
        }


        [HttpPost]
        public void SaveMediaSync(MediaSync mediaSync)
        {
            // Permitted SyncState transitions
            // For instance, if a user creates a new media, the state will be set to "new". If the same media is modified but has not been 
            // deployed yet, we need to keep the state "new" and not modify it with "modified"

            // If true, the mediaSync item will be deleted instead of being saved
            bool deleteContenSync = false;

            // Get the media sync current value
            var mediaSyncCurrentValue = GetMediaSync(mediaSync.TargetSiteId, mediaSync.MediaId);
            if (mediaSyncCurrentValue != null)
            {
                var syncStateCurrentValue = mediaSyncCurrentValue.SyncState;
                var warningMessage = false;

                switch (syncStateCurrentValue)
                {
                    case MediaSync.State.unknown:
                        switch (mediaSync.SyncState)
                        {
                            case MediaSync.State.modified:
                            case MediaSync.State.deleted:
                            case MediaSync.State.synced:
                            case MediaSync.State.created:
                            case MediaSync.State.pendingDelete:
                                // Ok
                                break;
                            case MediaSync.State.unknown:
                            default:
                                // This sync state transition should not happen!
                                warningMessage = true;
                                // Keep the current sync state
                                mediaSync.SyncState = MediaSync.State.unknown;
                                break;
                        }
                        break;

                    case MediaSync.State.created:
                        switch (mediaSync.SyncState)
                        {
                            case MediaSync.State.synced:
                                // Ok
                                break;
                            case MediaSync.State.modified:
                                // The media has not yet been synchronized , so must be kept as it is
                                mediaSync.SyncState = MediaSync.State.created;
                                break;
                            case MediaSync.State.pendingDelete:
                            case MediaSync.State.deleted:
                                // The media has not yet been synchronized, so must never be created or synchronized
                                deleteContenSync = true;
                                break;
                            case MediaSync.State.unknown:
                            case MediaSync.State.created:
                            default:
                                // This sync state transition should not happen!
                                warningMessage = true;
                                // Keep the current sync state
                                mediaSync.SyncState = MediaSync.State.created;
                                break;
                        }
                        break;

                    case MediaSync.State.modified:
                        switch (mediaSync.SyncState)
                        {
                            case MediaSync.State.synced:
                            case MediaSync.State.deleted:
                            case MediaSync.State.modified:
                            case MediaSync.State.pendingDelete:
                                // Ok
                                break;
                            case MediaSync.State.unknown:
                            case MediaSync.State.created:
                            default:
                                // This sync state transition should not happen!
                                warningMessage = true;
                                // Keep the current sync state
                                mediaSync.SyncState = MediaSync.State.modified;
                                break;
                        }
                        break;

                    case MediaSync.State.pendingDelete:
                        switch (mediaSync.SyncState)
                        {
                            case MediaSync.State.deleted:
                                // Ok
                                break;
                            case MediaSync.State.synced:
                            case MediaSync.State.modified:
                                // This sync state transition should not happen!
                                // But allow the state transition in order to allow to restoring wrong deletings
                                warningMessage = true;
                                break;
                            case MediaSync.State.unknown:
                            case MediaSync.State.created:
                            case MediaSync.State.pendingDelete:
                            default:
                                // This sync state transition should not happen!
                                warningMessage = true;
                                // Keep the current sync state
                                mediaSync.SyncState = MediaSync.State.pendingDelete;
                                break;
                        }
                        break;

                    case MediaSync.State.deleted:
                        switch (mediaSync.SyncState)
                        {
                            case MediaSync.State.synced:
                                // The media has been updated in the target site, so delete the mediaSync since it is not necessary anymore
                                deleteContenSync = true;
                                break;
                            case MediaSync.State.unknown:
                            case MediaSync.State.created:
                            case MediaSync.State.modified:
                            case MediaSync.State.deleted:
                            case MediaSync.State.pendingDelete:
                            default:
                                // This sync state transition should not happen!
                                warningMessage = true;
                                // Keep the current sync state
                                mediaSync.SyncState = MediaSync.State.deleted;
                                break;
                        }
                        break;

                    case MediaSync.State.synced:
                        switch (mediaSync.SyncState)
                        {
                            case MediaSync.State.modified:
                            case MediaSync.State.deleted:
                            case MediaSync.State.synced:
                            case MediaSync.State.pendingDelete:
                                // Ok
                                break;
                            case MediaSync.State.unknown:
                            case MediaSync.State.created:
                            default:
                                // This sync state transition should not happen!
                                warningMessage = true;
                                // Keep the current sync state
                                mediaSync.SyncState = MediaSync.State.synced;
                                break;
                        }
                        break;

                    default:
                        // This sync state transition should not happen!
                        warningMessage = true;
                        break;
                }

                if (warningMessage)
                    LogHelper.Warn<DeployApiController>("Media id = {0}, the sync state transition '{1}->{2}' should not happen! ", () => mediaSync.MediaId, () => syncStateCurrentValue.ToString(), () => mediaSync.SyncState.ToString());

            }

            // Save/Delete item
            var db = ApplicationContext.DatabaseContext.Database;
            if (deleteContenSync)
            {
                db.Delete(mediaSync);
            }
            else
            {
                db.Save(mediaSync);
            }

        }

        [HttpPost]
        public bool DeleteMediaSync(MediaSync mediaSync)
        {
            var result = true;
            var db = ApplicationContext.DatabaseContext.Database;
            if (db.Delete(mediaSync) != 1)
                result = false;
            return result;
        }

        /// <summary>
        /// Retrieve all mediaSync items from the database which have the state 'pendingDelete' and check whether their associated medias have been physically deleted from the database
        /// This two step mediaSync delete process is necesssary because the delete event is triggered before the media is really deleted, so it is not possible when the event is triggered if the
        /// media has been deleted. 
        /// We don't trust the delete event since once it was triggered for ALL medias available in the database (loosing all the sync info)!! And because it could happen again, we want to be protected
        /// </summary>
        private void persistPendingDeleteMediaSyncs()
        {
            var db = ApplicationContext.DatabaseContext.Database;
            var sql = Sql.Builder
                .Select("*")
                .From<MediaSync>()
                .Where<MediaSync>(x => x._SyncState == (int)MediaSync.State.pendingDelete);
            var mediaSyncs = db.Fetch<MediaSync>(sql).ToList<MediaSync>();
            foreach (var mediaSync in mediaSyncs)
            {
                // Mark the mediaSync as deleted
                if (MediaHelper.IsMediaDeleted(mediaSync.MediaId))
                {
                    mediaSync.SyncState = MediaSync.State.deleted;
                    // The method SaveMediaSync() must not used in order to avoid an infinite loop
                    db.Save(mediaSync);
                }
            }
        }

        #endregion

        #region CurrentSite

        [HttpGet]
        public CurrentSite GetCurrentSite()
        {
            CurrentSite result = null;
            var db = ApplicationContext.DatabaseContext.Database;
            var sql = Sql.Builder
                .Select("*")
                .From<CurrentSite>();
            result = db.Fetch<CurrentSite>(sql).FirstOrDefault();
            return result;
        }

        [HttpPost]
        public int SaveCurrentSite(CurrentSite currentSite)
        {
            var result = -1;

            var db = ApplicationContext.DatabaseContext.Database;
            if (db.IsNew(currentSite))
            {
                int.TryParse(db.Insert(currentSite).ToString(), out result);
            }
            else
            {
                db.Update(currentSite);
                result = currentSite.Id;
            }

            return result;
        }

        #endregion

        #region TargetSite

        [HttpGet]
        public IEnumerable<TargetSite> GetTargetSites()
        {
            IEnumerable<TargetSite> result;
            var db = ApplicationContext.DatabaseContext.Database;
            var sql = Sql.Builder
                .Select("*")
                .From<TargetSite>();
            result = db.Fetch<TargetSite>(sql);
            return result;
        }

        [HttpGet]
        public TargetSite GetTargetSite(int targetSiteId)
        {
            TargetSite result = null;
            var db = ApplicationContext.DatabaseContext.Database;
            var sql = Sql.Builder
                .Select("*")
                .From<TargetSite>()
                .Where<TargetSite>(x => x.Id == targetSiteId);
            result = db.Fetch<TargetSite>(sql).FirstOrDefault();
            return result;
        }

        [HttpPost]
        public int SaveTargetSite(TargetSite targetSite)
        {
            var result = -1;

            var db = ApplicationContext.DatabaseContext.Database;
            if (db.IsNew(targetSite))
            {
                int.TryParse(db.Insert(targetSite).ToString(), out result);
            }
            else
            {
                db.Update(targetSite);
                result = targetSite.Id;
            }

            return result;
        }

        [HttpGet]
        public bool GetTargetSiteDeleted(int targetSiteId)
        {
            var result = true;

            // Check whether the target site exists
            var targetSite = GetTargetSite(targetSiteId);
            if (targetSite == null)
                return false;

            // Delete all ContentSyncs of the target site
            foreach (var contentSync in GetAllContentSyncsByTargetSite(targetSiteId, false))
            {
                if (!DeleteContentSync(contentSync))
                    result = false;
            }

            // Delete the target site
            var db = ApplicationContext.DatabaseContext.Database;
            if (db.Delete(targetSite) != 1)
                result = false;

            return result;
        }

        #endregion

        #region Content

        [HttpGet]
        public dynamic GetContentById(int contentId)
        {
            dynamic result = null;
            // Get the content
            IContent content = Services.ContentService.GetById(contentId);

            if (content != null)
            {
                // Convert content to dynamic
                result = DynamicHelper.ToDynamic(content);

                // Add a new property with the contentType Alias
                IContentType contentType = Services.ContentTypeService.GetContentType(content.ContentTypeId);
                result.ContentTypeAlias = contentType.Alias;

                // Media files collection 
                var mediaFiles = new Dictionary<string, string>();

                // Get all properties which type is 'UploadField' DataType 
                var UploadFields = contentType.CompositionPropertyTypes.Where(x => x.PropertyEditorAlias == Constants.PropertyEditors.UploadFieldAlias);
                if (UploadFields.Any())
                {
                    //Loop through those properties to get the media data and serialize it
                    foreach (var property in UploadFields)
                    {
                        var propertyValue = content.Properties[property.Alias].Value.ToString();
                        if (!string.IsNullOrWhiteSpace(propertyValue))
                        {
                            // Media file 
                            mediaFiles.Add(propertyValue, ContentAndMediaFileHelper.ReadMediaFile(propertyValue));
                        }
                    }
                }

                // Get all properties which type is 'ImageCropper' DataType 
                var ImageCropperFields = contentType.CompositionPropertyTypes.Where(x => x.PropertyEditorAlias == Constants.PropertyEditors.ImageCropperAlias);
                if (ImageCropperFields.Any())
                {
                    //Loop through those properties to get the media data and serialize it
                    foreach (var property in ImageCropperFields)
                    {
                        var propertyValue = content.Properties[property.Alias].Value.ToString();
                        var propertyValueFilePath = ContentAndMediaFileHelper.ImageCropperGetImageFilePath(propertyValue);
                        if (!string.IsNullOrWhiteSpace(propertyValueFilePath))
                        {
                            mediaFiles.Add(propertyValueFilePath, ContentAndMediaFileHelper.ReadMediaFile(propertyValueFilePath));
                        }
                    }
                }

                // If there are medias then add them to the Content
                if (mediaFiles.Count > 0)
                    result.MediaFiles = mediaFiles;
            }

            return result;
        }

        #endregion

        #region Media

        [HttpGet]
        public dynamic GetMediaById(int mediaId)
        {
            dynamic result = null;
            // Get the media
            IMedia media = Services.MediaService.GetById(mediaId);

            if (media != null)
            {
                // Convert Media to dynamic
                result = DynamicHelper.ToDynamic(media);

                // Add a new property with the mediaType Alias
                IMediaType mediaType = Services.ContentTypeService.GetMediaType(media.ContentTypeId);
                result.ContentTypeAlias = mediaType.Alias;

                // Media files collection 
                var mediaFiles = new Dictionary<string, string>();

                // Get all properties which type is 'UploadField' DataType 
                var UploadFields = mediaType.CompositionPropertyTypes.Where(x => x.PropertyEditorAlias == Constants.PropertyEditors.UploadFieldAlias);
                if (UploadFields.Any())
                {
                    //Loop through those properties to get the media data and serialize it
                    foreach (var property in UploadFields)
                    {
                        var propertyValue = media.Properties[property.Alias].Value.ToString();
                        if (!string.IsNullOrWhiteSpace(propertyValue))
                        {
                            // Media file 
                            mediaFiles.Add(propertyValue, ContentAndMediaFileHelper.ReadMediaFile(propertyValue));
                        }
                    }
                }

                // Get all properties which type is 'ImageCropper' DataType 
                var ImageCropperFields = mediaType.CompositionPropertyTypes.Where(x => x.PropertyEditorAlias == Constants.PropertyEditors.ImageCropperAlias);
                if (ImageCropperFields.Any())
                {
                    //Loop through those properties to get the media data and serialize it
                    foreach (var property in ImageCropperFields)
                    {
                        var propertyValue = media.Properties[property.Alias].Value.ToString();
                        var propertyValueFilePath = ContentAndMediaFileHelper.ImageCropperGetImageFilePath(propertyValue);
                        if (!string.IsNullOrWhiteSpace(propertyValueFilePath))
                        {
                            mediaFiles.Add(propertyValueFilePath, ContentAndMediaFileHelper.ReadMediaFile(propertyValueFilePath));
                        }
                    }
                }

                // If there are medias then add them to the media
                if (mediaFiles.Count > 0)
                    result.MediaFiles = mediaFiles;
            }

            return result;
        }

        #endregion


    }

}




