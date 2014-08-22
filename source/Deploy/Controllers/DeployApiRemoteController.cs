using System;
using System.Web;
using System.Web.Mvc;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Web.Http;
using System.Net.Http;
using System.Text;
using System.IO;

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

using Deploy.ActionFilters;

namespace Deploy.Controllers.Api
{

    [PluginController("Deploy")]
    [IsBackOffice]
    [AllowCrossSiteJson]
    public class DeployApiRemoteController : UmbracoApiController
    {

        /// <summary>
        /// This method is like a "Ping" method in order to make sure the client can reach this API
        /// </summary>
        /// <returns>Always returns true</returns>
        [System.Web.Http.HttpGet]
        [System.Web.Http.HttpOptions]
        public bool TestAccess()
        {
            return true;
        }

        #region "CONTENT"

        /// <summary>
        /// This type is used to send back the deployment results
        /// </summary>
        public class DeployContentResult
        {
            public Guid? ContentGuid = null;
            public string errorMessage = string.Empty;
        };

        /// <summary>
        /// Deploys a content (create, update or delete)
        /// </summary>
        /// <param name="dataXml"></param>
        /// <returns></returns>
        [System.Web.Http.HttpPost]
        [System.Web.Http.HttpOptions]
        public dynamic DeployContent([FromBody]string dataXml)
        {
            if (HttpContext.Current.Request.HttpMethod.Equals(HttpMethod.Options.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                // Returns an empty string instead of an error message (dynamic type) because if not, firefox doesn't do a POST after the OPTIONS request ¿Is it a bug?
                return string.Empty;
            }

            var result = new DeployContentResult();

            try
            {

                // Check wheter the current site allows remote deployment
                var deployApi = new DeployApiController();
                var currentSite = deployApi.GetCurrentSite();
                if (currentSite == null || !currentSite.Enabled)
                {
                    result.errorMessage = "The target site doesn't allow remote deployment or is not correctly set up";
                    return result;
                }

                // Initializations
                dynamic dataJson = null;
                dynamic sourceContent = null;
                ContentSync sourceContentSync = null;
                ContentSync sourceContentParentSync = null;
                ContentDeploymentSettings contentDeploymentSettings = null;
                IContentType sourceContentType = null;
                ITemplate sourceContentTemplate = null;
                IContent targetParentContent = null;
                // Used to store the media files from the sourceContent
                Dictionary<string, string> sourceMediaFiles = null;

                // Parse Json to extract content data            
                if (dataXml == null)
                {
                    result.errorMessage = "The parameter 'dataXml' is empty";
                    return result;
                }
                dataJson = JObject.Parse(dataXml);
                if (dataJson == null)
                {
                    result.errorMessage = "The parameter 'dataXml' doesn't contain valid data";
                    return result;
                }

                // Check whether the contentDeploymentSettings parameter is valid
                if (dataJson.contentDeploymentSettings == null)
                {
                    result.errorMessage = "The parameter 'contentDeploymentSettings' is empty";
                    return result;
                }
                contentDeploymentSettings = JsonConvert.DeserializeObject<ContentDeploymentSettings>(JsonConvert.SerializeObject(dataJson.contentDeploymentSettings));
                if (contentDeploymentSettings == null)
                {
                    result.errorMessage = "The parameter 'contentDeploymentSettings' is not valid";
                    return result;
                }

                // Check whether the contentSync parameter is valid
                if (dataJson.contentSync == null)
                {
                    result.errorMessage = "The parameter 'ContentSync' is empty";
                    return result;
                }
                sourceContentSync = JsonConvert.DeserializeObject<ContentSync>(JsonConvert.SerializeObject(dataJson.contentSync));
                if (sourceContentSync == null)
                {
                    result.errorMessage = "The parameter 'ContentSync' is not valid";
                    return result;
                }

                // Get the data of the content to deploy (delete, update, create)
                // If the content is marked for deletion then there is no data regarding updating/creating content, but there is the data of the content before it was deleted in the origin site
                if (sourceContentSync.SyncState == ContentSync.State.deleted)
                {
                    // Content to delete
                    if (dataJson.contentSync.DeletedContentData == null)
                    {
                        result.errorMessage = "The property 'DeletedContentData' is empty";
                        return result;
                    }
                    sourceContent = JObject.Parse(dataJson.contentSync.DeletedContentData.ToString());
                    if (sourceContent == null)
                    {
                        result.errorMessage = "The property 'DeletedContentData' is not valid";
                        return result;
                    }
                }
                else
                {
                    // Content to update or create
                    sourceContent = dataJson.content;
                    if (sourceContent == null)
                    {
                        result.errorMessage = "The parameter 'Content' is empty";
                        return result;
                    }
                }

                // Check whether the content to deploy exists or not in the target site
                // Even if the content is marked as 'created' we check if it exists, to avoid to create duplicate contents!
                // Even if the content is marked as 'modified' we check if it exists because it could have been deleted 
                bool contentToDeployExist = false;
                if (sourceContentSync.TargetContentGuid != Guid.Empty)
                {
                    // Check whether the content exist using the targetContentGuid
                    IContent foundContent = Services.ContentService.GetById(sourceContentSync.TargetContentGuid);
                    if (foundContent != null)
                    {
                        contentToDeployExist = true;
                    }
                }

                if (!contentToDeployExist)
                {
                    // Try to find the content type using the Key attribute of the original content
                    IContent foundContent = Services.ContentService.GetById(new Guid(sourceContent.Key.ToString()));
                    if (foundContent != null)
                    {
                        sourceContentSync.TargetContentGuid = foundContent.Key;
                        contentToDeployExist = true;
                    }
                }

                if (!contentToDeployExist)
                {
                    // Try to find the content in the targetSite by calling the method resolveNotFoundContent() that could be customized (overriden) by users
                    Guid? foundContentGuid = resolveNotFoundContent(sourceContent);
                    if (foundContentGuid != null)
                    {
                        sourceContentSync.TargetContentGuid = foundContentGuid.Value;
                        contentToDeployExist = true;
                    }

                }

                // If the content wasn't found and is a 'deleted' command 
                if (!contentToDeployExist && sourceContentSync.SyncState == ContentSync.State.deleted)
                {
                    // --------------------- TODO: Send this settings attached to the contentSync instead of using an hard-coded value

                    // Check the settings to know whether to display an error message or simply continue ahead 
                    if (contentDeploymentSettings.DeletingContentIfNotFoundNoMessageError)
                    {
                        // Display an error message
                        result.errorMessage = "The content could not be found in the target site";
                        return result;
                    }
                    else
                    {
                        // In the case of a deleted content there is nothing to do since the content doesn't exist (couldn't be found)
                        // Assign an empty GUID in order to avoid the UI displaying an error message to the user
                        result.ContentGuid = Guid.Empty;
                        return result;
                    }

                }

                // If the content wasn't found and is a 'modified' command
                if (!contentToDeployExist && sourceContentSync.SyncState == ContentSync.State.modified)
                {
                    // --------------------- TODO: Add this setting in the UI (could be different for each target site) to allow the user to enable/disable this feature

                    // Check the settings to know whether to display an error message or simply continue ahead 
                    if (!contentDeploymentSettings.ModifyingContentIfNotFoundCreateContent)
                    {
                        // Display an error message
                        result.errorMessage = "The content could not be found in the target site";
                        return result;
                    }
                    else
                    {
                        // We convert the 'modified' command into a 'created' command
                        sourceContentSync.SyncState = ContentSync.State.created;
                    }

                }

                // Even if the content is marked as 'created' we need to check if it already exists, in order to avoid to create duplicate contents!
                if (contentToDeployExist && sourceContentSync.SyncState == ContentSync.State.created)
                {
                    // We convert the 'created' command into a 'modified' command in order to avoid duplicates
                    sourceContentSync.SyncState = ContentSync.State.modified;
                }

                // If the content has to be deleted then bypass the following checks
                if (sourceContentSync.SyncState != ContentSync.State.deleted)
                {
                    //// Content to deploy
                    //sourceContent = dataJson.content;
                    //if (sourceContent == null)
                    //{
                    //    result.errorMessage = "The parameter 'Content' is empty";
                    //    return result;
                    //}

                    // Check whether the parent is mapped and exists
                    // The parameter parentContentSync could be null. That means the parent node is the root node or recycle bin node.
                    if (dataJson.parentContentSync != null && dataJson.parentContentSync.TargetContentGuid.ToString() != Guid.Empty.ToString())
                    {
                        // Mapped parent node
                        sourceContentParentSync = JsonConvert.DeserializeObject<ContentSync>(JsonConvert.SerializeObject(dataJson.parentContentSync));
                        if (sourceContentParentSync.TargetContentGuid != null && sourceContentParentSync.TargetContentGuid != Guid.Empty)
                            targetParentContent = Services.ContentService.GetById(sourceContentParentSync.TargetContentGuid);
                        if (targetParentContent == null)
                        {
                            result.errorMessage = "The parent node doesn't exist in the target site.";
                            return result;
                        }
                    }
                    else
                    {
                        // Check whether the Parent node is root or recycle bin
                        if (sourceContent.ParentId != Constants.System.Root && sourceContent.ParentId != Constants.System.RecycleBinContent)
                        {
                            result.errorMessage = "The parent node is not mapped into the target site.";
                            return result;
                        }
                    }

                    // Check whether the node's Content Type exists
                    sourceContentType = Services.ContentTypeService.GetContentType(sourceContent.ContentTypeAlias.ToString());

                    if (sourceContentType == null)
                    {
                        result.errorMessage = string.Format("The content type '{0}' doesn't exist in the target site", sourceContent.ContentTypeAlias.ToString());
                        return result;
                    }

                    // Check whether the content as a template 
                    if (sourceContent.Template != null && sourceContent.Template.Alias != null)
                    {
                        // Check whether the template exists
                        sourceContentTemplate = Services.FileService.GetTemplate(sourceContent.Template.Alias.ToString());
                        if (sourceContentTemplate == null)
                        {
                            result.errorMessage = string.Format("The content's template '{0}' doesn't exist in the target site", sourceContent.Template.Alias.ToString());
                            return result;
                        }

                        // Check whether the node content type allows the template 
                        if (sourceContentType.AllowedTemplates.Where(x => x.Alias == sourceContentTemplate.Alias).Count() == 0)
                        {
                            result.errorMessage = string.Format("The content's template '{0}' is not allowed for the content type '{1}' in the target site", sourceContentTemplate.Name, sourceContentType.Name);
                            return result;
                        }
                    }
                }

                // Media file system (required to access media files)
                MediaFileSystem mediaFileSystem = FileSystemProviderManager.Current.GetFileSystemProvider<MediaFileSystem>();

                var warningMessage = false;
                switch ((ContentSync.State)sourceContentSync.SyncState)
                {
                    case ContentSync.State.unknown:
                        // Unexpected sync state !
                        warningMessage = true;
                        break;

                    case ContentSync.State.created:

                        // Create a new content node
                        var contentParentId = 0;
                        if (sourceContent.ParentId != Constants.System.Root && sourceContent.ParentId != Constants.System.RecycleBinContent)
                        {
                            contentParentId = targetParentContent.Id;
                        }
                        else
                        {
                            contentParentId = (int)sourceContent.ParentId;
                        }
                        IContent contentToCreate = Services.ContentService.CreateContent(sourceContent.Name.ToString(), contentParentId, sourceContentType.Alias);

                        // Check whether the content has been created properly
                        if (contentToCreate == null || contentToCreate.Key == null)
                        {
                            result.errorMessage = "Error creating new content in the target site";
                            return result;
                        }

                        // Assign the same Key
                        contentToCreate.Key = new Guid(sourceContent.Key.ToString());

                        // Get media files' data from the source
                        if (sourceContent.MediaFiles != null)
                            sourceMediaFiles = sourceContent.MediaFiles.ToObject<Dictionary<string, string>>();

                        // Update properties
                        foreach (var property in sourceContent.Properties)
                        {
                            foreach (var propertyCollection in property.Children())
                            {
                                foreach (var propertyItem in propertyCollection.Children())
                                {
                                    // Check wheter the target to update contains the property
                                    //if (!contentToUpdate.HasProperty(propertyItem.Alias.Value))
                                    if (!contentToCreate.Properties.Contains(propertyItem.Alias.Value))
                                    {
                                        result.errorMessage = string.Format("The target content doesn't have any property with the alias'{0}'", propertyItem.Alias);
                                        return result;

                                    }
                                    // --------------------- TODO: Check wheter the property has the same type
                                    // for this we will use content .PropertyTypes() 

                                    // Update the property value
                                    ((Content)contentToCreate).SetValue(propertyItem.Alias.Value, propertyItem.Value.Value);
                                }
                            }
                        }

                        if (sourceContent.CreateDate != null)
                            contentToCreate.CreateDate = Convert.ToDateTime(sourceContent.CreateDate);

                        if (sourceContent.ExpireDate != null)
                            contentToCreate.ExpireDate = Convert.ToDateTime(sourceContent.ExpireDate);

                        if (sourceContent.Language != null)
                            contentToCreate.Language = sourceContent.Language;

                        contentToCreate.Name = sourceContent.Name;

                        if (sourceContent.ReleaseDate != null)
                            contentToCreate.ReleaseDate = Convert.ToDateTime(sourceContent.ReleaseDate);

                        contentToCreate.SortOrder = sourceContent.SortOrder;

                        if (sourceContentTemplate != null)
                            contentToCreate.Template = sourceContentTemplate;

                        if (sourceContent.UpdateDate != null)
                            contentToCreate.UpdateDate = Convert.ToDateTime(sourceContent.UpdateDate);

                        // Check whether it is necesary to create media items for properties (upload control, cropimage)
                        // All these operations have to be performed inside a try/catch block in order to detect any error and delete the content we previously created!!
                        try
                        {
                            if (sourceMediaFiles != null)
                            {
                                // First of all, save the content in order to create the properties with Ids (since it is a new content). That is not necesary when
                                // updating an existing content!
                                Services.ContentService.Save(contentToCreate);

                                // Look up for media properties and create media items
                                foreach (var property in sourceContent.Properties)
                                {
                                    foreach (var propertyCollection in property.Children())
                                    {
                                        foreach (var propertyItem in propertyCollection.Children())
                                        {
                                            // Check wheter the property's datatype is 'UploadField'. 
                                            if (sourceContentType.CompositionPropertyTypes.Where(x => x.Alias == propertyItem.Alias.Value && x.PropertyEditorAlias == Constants.PropertyEditors.UploadFieldAlias).Count() > 0)
                                            {
                                                if (sourceMediaFiles != null && sourceMediaFiles.ContainsKey(propertyItem.Value.Value))
                                                {
                                                    string fileData = sourceMediaFiles[propertyItem.Value.Value];

                                                    // Create the new media file
                                                    propertyItem.Value.Value = ContentAndMediaFileHelper.SaveContentPropertyFile(contentToCreate, propertyItem.Alias.Value, System.IO.Path.GetFileName(propertyItem.Value.Value), ContentAndMediaFileHelper.GetMediaFileStream(fileData));

                                                    // Update the property value
                                                    ((Content)contentToCreate).SetValue(propertyItem.Alias.Value, propertyItem.Value.Value);
                                                }
                                                else
                                                {
                                                    // Update the property value
                                                    ((Content)contentToCreate).SetValue(propertyItem.Alias.Value, string.Empty);
                                                }
                                            }

                                            // Check wheter the property's datatype is 'ImageCropper'. 
                                            if (sourceContentType.CompositionPropertyTypes.Where(x => x.Alias == propertyItem.Alias.Value && x.PropertyEditorAlias == Constants.PropertyEditors.ImageCropperAlias).Count() > 0)
                                            {
                                                // Extract the file name from the Json data
                                                var propertyItemValueFilePath = ContentAndMediaFileHelper.ImageCropperGetImageFilePath(propertyItem.Value.Value.ToString());

                                                if (sourceMediaFiles != null && sourceMediaFiles.ContainsKey(propertyItemValueFilePath))
                                                {
                                                    string fileData = sourceMediaFiles[propertyItemValueFilePath];

                                                    // Create the new media file
                                                    var newMediaFilePath = ContentAndMediaFileHelper.SaveContentPropertyFile(contentToCreate, propertyItem.Alias.Value, System.IO.Path.GetFileName(propertyItemValueFilePath), ContentAndMediaFileHelper.GetMediaFileStream(fileData));
                                                    propertyItem.Value.Value = ContentAndMediaFileHelper.ImageCropperUpdateImageFilePath(propertyItem.Value.Value, newMediaFilePath);

                                                    // Update the property value
                                                    ((Content)contentToCreate).SetValue(propertyItem.Alias.Value, propertyItem.Value.Value);
                                                }
                                                else
                                                {
                                                    // Update the property value
                                                    ((Content)contentToCreate).SetValue(propertyItem.Alias.Value, string.Empty);
                                                }
                                            }

                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            Services.ContentService.Delete(contentToCreate);
                            contentToCreate = null;

                            // Throw the exception in order to let know the user about the error message
                            throw;
                        }

                        // Check whether the contentToCreate exists. That means there were no errors with the media files.
                        if (contentToCreate != null)
                        {
                            if ((bool)sourceContent.Published)
                                Services.ContentService.SaveAndPublishWithStatus(contentToCreate);
                            else
                                Services.ContentService.Save(contentToCreate);

                            result.ContentGuid = contentToCreate.Key;
                        }

                        break;

                    case ContentSync.State.modified:

                        //// If the content is not already mapped, resolve the mapping 
                        //if (sourceContentSync.TargetContentGuid == Guid.Empty)
                        //{
                        //    sourceContentSync.TargetContentGuid = resolveNonMappedContent(sourceContent);
                        //    if (sourceContentSync.TargetContentGuid == null || sourceContentSync.TargetContentGuid == Guid.Empty)
                        //    {
                        //        result.errorMessage = "Error resolving Non-Mapped content in the target site";
                        //        return result;
                        //    }
                        //}

                        // Load the content
                        var contentToUpdate = Services.ContentService.GetById(sourceContentSync.TargetContentGuid);
                        if (contentToUpdate == null)
                        {
                            result.errorMessage = "Couldn't find the content in the target site";
                            return result;
                        }

                        // Check whether the content type is the same and if it isn't then change it
                        if (contentToUpdate.ContentType.Alias != sourceContent.ContentTypeAlias.ToString())
                            contentToUpdate.ChangeContentType(sourceContentType);


                        // Check whether the parent node is the same or the content has been moved
                        if (targetParentContent != null && contentToUpdate.ParentId != targetParentContent.Id)
                        {
                            if (targetParentContent.Id != Constants.System.RecycleBinContent)
                            {
                                Services.ContentService.Move(contentToUpdate, targetParentContent.Id);
                            }
                        }

                        // Check whether the node is trashed
                        if (sourceContent.ParentId == Constants.System.RecycleBinContent)
                        {
                            Services.ContentService.MoveToRecycleBin(contentToUpdate);
                        }


                        // Get media files' data from the source
                        if (sourceContent.MediaFiles != null)
                            sourceMediaFiles = sourceContent.MediaFiles.ToObject<Dictionary<string, string>>();

                        // Update properties
                        foreach (var property in sourceContent.Properties)
                        {
                            foreach (var propertyCollection in property.Children())
                            {
                                foreach (var propertyItem in propertyCollection.Children())
                                {
                                    // Check wheter the target to update contains the property
                                    //if (!contentToUpdate.HasProperty(propertyItem.Alias.Value))
                                    if (!contentToUpdate.Properties.Contains(propertyItem.Alias.Value))
                                    {
                                        result.errorMessage = string.Format("The target content doesn't have any property with the alias'{0}'", propertyItem.Alias);
                                        return result;

                                    }

                                    // --------------------- TODO: Check wheter the property has the same type
                                    // for this we will use content .PropertyTypes() 


                                    // Check wheter the property's datatype is 'UploadField' in order to manage the asociated media file
                                    if (sourceContentType.CompositionPropertyTypes.Where(x => x.Alias == propertyItem.Alias.Value && x.PropertyEditorAlias == Constants.PropertyEditors.UploadFieldAlias).Count() > 0)
                                    {
                                        // Check whether that we have the media in the sourceContent's collection of medias (sent within the content xml)
                                        if (sourceMediaFiles != null && sourceMediaFiles.ContainsKey(propertyItem.Value.Value))
                                        {
                                            // Check whether the property has currently a media item 
                                            var propertyCurrentValue = contentToUpdate.Properties.First(x => x.Alias == propertyItem.Alias.Value).Value.ToString();

                                            // Get the media file content
                                            string newMediaFileData = sourceMediaFiles[propertyItem.Value.Value];

                                            // Check whether the media has changed in which case we need to update
                                            string currentMediaFileData = ContentAndMediaFileHelper.ReadMediaFile(propertyCurrentValue);
                                            if (!currentMediaFileData.Equals(newMediaFileData))
                                            {
                                                // Delete the media file if there is one
                                                ContentAndMediaFileHelper.DeleteMediaFile(propertyCurrentValue);

                                                // Create the new media file
                                                propertyItem.Value.Value = ContentAndMediaFileHelper.SaveContentPropertyFile(contentToUpdate, propertyItem.Alias.Value, System.IO.Path.GetFileName(propertyItem.Value.Value), ContentAndMediaFileHelper.GetMediaFileStream(newMediaFileData));
                                            }
                                            else
                                            {
                                                // Both media are equals, so no need to update, but need to keep the current MediaFilePath and not the mediaFilePath sent
                                                propertyItem.Value.Value = propertyCurrentValue;
                                            }
                                        }
                                        // Take in account the case in which the media item has been deleted 
                                        else if (string.IsNullOrWhiteSpace(propertyItem.Value.Value))
                                        {
                                            var propertyCurrentValue = contentToUpdate.Properties.First(x => x.Alias == propertyItem.Alias.Value).Value.ToString();

                                            // Delete the media file if there is one
                                            ContentAndMediaFileHelper.DeleteMediaFile(propertyCurrentValue);
                                        }
                                    }

                                    // Check wheter the property's datatype is 'ImageCropper' in order to manage the asociated media file
                                    if (sourceContentType.CompositionPropertyTypes.Where(x => x.Alias == propertyItem.Alias.Value && x.PropertyEditorAlias == Constants.PropertyEditors.ImageCropperAlias).Count() > 0)
                                    {
                                        // Extract the file name from the Json data
                                        var propertyItemValueFilePath = ContentAndMediaFileHelper.ImageCropperGetImageFilePath(propertyItem.Value.ToString());

                                        // Check whether that we have the media in the sourceContent's collection of medias (sent within the content xml)
                                        if (!string.IsNullOrWhiteSpace(propertyItemValueFilePath) && sourceMediaFiles != null && sourceMediaFiles.ContainsKey(propertyItemValueFilePath))
                                        {
                                            // Check whether the property has currently a media item 
                                            var propertyCurrentValue = contentToUpdate.Properties.First(x => x.Alias == propertyItem.Alias.Value).Value;

                                            // Extract the file name from the Json data
                                            var propertyCurrentValueFilePath = ContentAndMediaFileHelper.ImageCropperGetImageFilePath(propertyCurrentValue.ToString());

                                            // Get the media file content
                                            string newMediaFileData = sourceMediaFiles[propertyItemValueFilePath];

                                            // Check whether the media has changed in which case we need to update
                                            string currentMediaFileData = ContentAndMediaFileHelper.ReadMediaFile(propertyCurrentValueFilePath);
                                            if (!currentMediaFileData.Equals(newMediaFileData))
                                            {
                                                // Delete the media file if there is one
                                                ContentAndMediaFileHelper.DeleteMediaFile(propertyCurrentValueFilePath);

                                                // Create the new media file
                                                var newMediaFilePath = ContentAndMediaFileHelper.SaveContentPropertyFile(contentToUpdate, propertyItem.Alias.Value, System.IO.Path.GetFileName(propertyItemValueFilePath), ContentAndMediaFileHelper.GetMediaFileStream(newMediaFileData));
                                                propertyItem.Value.Value = ContentAndMediaFileHelper.ImageCropperUpdateImageFilePath(propertyItem.Value.Value, newMediaFilePath);
                                            }
                                            else
                                            {
                                                // Both media are equals, so no need to update, but need to keep the current MediaFilePath and not the mediaFilePath sent
                                                propertyItem.Value.Value = ContentAndMediaFileHelper.ImageCropperUpdateImageFilePath(propertyCurrentValue.ToString(), propertyCurrentValueFilePath);
                                            }
                                        }
                                        // Take in account the case in which the media item has been deleted 
                                        else if (string.IsNullOrWhiteSpace(propertyItem.Value.Value))
                                        {
                                            var propertyCurrentValue = contentToUpdate.Properties.First(x => x.Alias == propertyItem.Alias.Value).Value.ToString();
                                            var propertyCurrentValueFilePath = ContentAndMediaFileHelper.ImageCropperGetImageFilePath(propertyCurrentValue);
                                            // Delete the media file if there is one
                                            ContentAndMediaFileHelper.DeleteMediaFile(propertyCurrentValueFilePath);
                                        }
                                    }

                                    // Update the property value
                                    ((Content)contentToUpdate).SetValue(propertyItem.Alias.Value, propertyItem.Value.Value);
                                }
                            }
                        }

                        if (sourceContent.CreateDate != null)
                            contentToUpdate.CreateDate = Convert.ToDateTime(sourceContent.CreateDate);

                        if (sourceContent.ExpireDate != null)
                            contentToUpdate.ExpireDate = Convert.ToDateTime(sourceContent.ExpireDate);

                        if (sourceContent.Language != null)
                            contentToUpdate.Language = sourceContent.Language;

                        contentToUpdate.Name = sourceContent.Name;

                        if (sourceContent.ReleaseDate != null)
                            contentToUpdate.ReleaseDate = Convert.ToDateTime(sourceContent.ReleaseDate);

                        contentToUpdate.SortOrder = sourceContent.SortOrder;

                        if (sourceContentTemplate != null)
                            contentToUpdate.Template = sourceContentTemplate;

                        if (sourceContent.UpdateDate != null)
                            contentToUpdate.UpdateDate = Convert.ToDateTime(sourceContent.UpdateDate);

                        if ((bool)sourceContent.Published)
                            Services.ContentService.SaveAndPublishWithStatus(contentToUpdate);
                        else
                            Services.ContentService.Save(contentToUpdate);

                        result.ContentGuid = contentToUpdate.Key;

                        break;

                    case ContentSync.State.deleted:

                        //// If the content is not already mapped, resolve the mapping 
                        //if (sourceContentSync.TargetContentGuid == Guid.Empty)
                        //{
                        //    sourceContentSync.TargetContentGuid = resolveNonMappedContent(sourceContent);
                        //    if (sourceContentSync.TargetContentGuid == null || sourceContentSync.TargetContentGuid == Guid.Empty)
                        //    {
                        //        result.errorMessage = "Error resolving Non-Mapped content in the target site";
                        //        return result;
                        //    }
                        //}

                        // Load the content
                        var contentToDelete = Services.ContentService.GetById(sourceContentSync.TargetContentGuid);
                        if (contentToDelete == null)
                        {
                            result.errorMessage = "Couldn't find the mapped content in the target site";
                            return result;
                        }

                        // Delete the content
                        var contentToDeleteKey = contentToDelete.Key;
                        Services.ContentService.Delete(contentToDelete);
                        result.ContentGuid = contentToDeleteKey;

                        break;

                    case ContentSync.State.synced:
                        // Unexpected sync state !
                        warningMessage = true;
                        break;

                    default:
                        // Unexpected sync state !
                        warningMessage = true;
                        break;
                }

                if (warningMessage)
                    LogHelper.Warn<DeployApiController>("Current site (acting as a target site), for Content id = {0}, got unexpected sync state : {1}! ", () => dataJson.content.Id, () => sourceContentSync.SyncState);

            }

            catch (Exception ex)
            {
                // Send the error message to the UI
                result.errorMessage = string.Format("Internal error <{0}>", ex.Message);
                return result;
            }

            return result;
        }

        /// <summary>
        /// Method called when a content is not found
        /// This method is virtual in order to allow the user to override it and use their own way to try to find a content (for instance, looking for a similar content using the content's name, id or any other property)
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        public virtual Guid? resolveNotFoundContent(dynamic content)
        {
            Guid? result = null;

            // TO BE customized

            return result;
        }

        public IContent GetContent(Guid contentGuid)
        {
            IContent result = null;

            var content = Services.ContentService.GetById(contentGuid);
            if (content != null)
                result = content;

            return result;
        }

        [System.Web.Http.HttpPost]
        [System.Web.Http.HttpOptions]
        public bool SaveContent(IContent content)
        {
            if (content != null)
            {
                return true;
                //Services.ContentService.Save(content);
            }
            return false;
        }

        public bool DeleteContent(Guid contentGuid)
        {
            var result = false;
            var content = Services.ContentService.GetById(contentGuid);
            if (content != null)
            {
                Services.ContentService.MoveToRecycleBin(content);
                result = true;
            }
            return result;
        }

        #endregion

        #region "MEDIA"

        /// <summary>
        /// This type is used to send back the deployment results
        /// </summary>
        public class DeployMediaResult
        {
            public Guid? MediaGuid = null;
            public string errorMessage = string.Empty;
        };

        /// <summary>
        /// Deploys a media (create, update or delete)
        /// </summary>
        /// <param name="dataXml"></param>
        /// <returns></returns>
        [System.Web.Http.HttpPost]
        [System.Web.Http.HttpOptions]
        public dynamic DeployMedia([FromBody]string dataXml)
        {

            if (HttpContext.Current.Request.HttpMethod.Equals(HttpMethod.Options.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                // Returns an empty string instead of an error message (dynamic type) because if not, firefox doesn't do a POST after the OPTIONS request ¿Is it a bug?
                return string.Empty;
            }

            var result = new DeployMediaResult();

            try
            {
                // Check wheter the current site allows remote deployment
                var deployApi = new DeployApiController();
                var currentSite = deployApi.GetCurrentSite();
                if (currentSite == null || !currentSite.Enabled)
                {
                    result.errorMessage = "The target site doesn't allow remote deployment or is not correctly set up";
                    return result;
                }

                // Initializations
                dynamic dataJson = null;
                dynamic sourceMedia = null;
                MediaSync sourceMediaSync = null;
                MediaSync sourceMediaParentSync = null;
                MediaDeploymentSettings mediaDeploymentSettings = null;
                IMediaType sourceMediaType = null;
                IMedia targetParentMedia = null;
                // Used to store the media files from the sourceMedia
                Dictionary<string, string> sourceMediaFiles = null;

                // Parse Json to extract media data            
                if (dataXml == null)
                {
                    result.errorMessage = "The parameter 'dataXml' is empty";
                    return result;
                }
                dataJson = JObject.Parse(dataXml);
                if (dataJson == null)
                {
                    result.errorMessage = "The parameter 'dataXml' doesn't contain valid data";
                    return result;
                }

                // Check whether the mediaDeploymentSettings parameter is valid
                if (dataJson.mediaDeploymentSettings == null)
                {
                    result.errorMessage = "The parameter 'mediaDeploymentSettings' is empty";
                    return result;
                }
                mediaDeploymentSettings = JsonConvert.DeserializeObject<MediaDeploymentSettings>(JsonConvert.SerializeObject(dataJson.mediaDeploymentSettings));
                if (mediaDeploymentSettings == null)
                {
                    result.errorMessage = "The parameter 'mediaDeploymentSettings' is not valid";
                    return result;
                }

                // Check whether the mediaSync parameter is valid
                if (dataJson.mediaSync == null)
                {
                    result.errorMessage = "The parameter 'MediaSync' is empty";
                    return result;
                }
                sourceMediaSync = JsonConvert.DeserializeObject<MediaSync>(JsonConvert.SerializeObject(dataJson.mediaSync));
                if (sourceMediaSync == null)
                {
                    result.errorMessage = "The parameter 'MediaSync' is not valid";
                    return result;
                }

                // Get the data of the media to deploy (delete, update, create)
                // If the media is marked for deletion then there is no data regarding updating/creating media, but there is the data of the media before it was deleted in the origin site
                if (sourceMediaSync.SyncState == MediaSync.State.deleted)
                {
                    // Media to delete
                    if (dataJson.mediaSync.DeletedMediaData == null)
                    {
                        result.errorMessage = "The property 'DeletedMediaData' is empty";
                        return result;
                    }
                    sourceMedia = JObject.Parse(dataJson.mediaSync.DeletedMediaData.ToString());
                    if (sourceMedia == null)
                    {
                        result.errorMessage = "The property 'DeletedMediaData' is not valid";
                        return result;
                    }
                }
                else
                {
                    // Media to update or create
                    sourceMedia = dataJson.media;
                    if (sourceMedia == null)
                    {
                        result.errorMessage = "The parameter 'Media' is empty";
                        return result;
                    }
                }

                // Check whether the media to deploy exists or not in the target site
                // Even if the media is marked as 'created' we check if it exists, to avoid to create duplicate medias!
                // Even if the media is marked as 'modified' we check if it exists because it could have been deleted 
                bool mediaToDeployExist = false;
                if (sourceMediaSync.TargetMediaGuid != Guid.Empty)
                {
                    // Check whether the media exist using the targetMediaGuid
                    IMedia foundMedia = Services.MediaService.GetById(sourceMediaSync.TargetMediaGuid);
                    if (foundMedia != null)
                    {
                        mediaToDeployExist = true;
                    }
                }

                if (!mediaToDeployExist)
                {
                    // Try to find the media type using the Key attribute of the original media
                    IMedia foundMedia = Services.MediaService.GetById(new Guid(sourceMedia.Key.ToString()));
                    if (foundMedia != null)
                    {
                        sourceMediaSync.TargetMediaGuid = foundMedia.Key;
                        mediaToDeployExist = true;
                    }
                }

                if (!mediaToDeployExist)
                {
                    // Try to find the media in the targetSite by calling the method resolveNotFoundMedia() that could be customized (overriden) by users
                    Guid? foundMediaGuid = resolveNotFoundMedia(sourceMedia);
                    if (foundMediaGuid != null)
                    {
                        sourceMediaSync.TargetMediaGuid = foundMediaGuid.Value;
                        mediaToDeployExist = true;
                    }

                }

                // If the media wasn't found and is a 'deleted' command 
                if (!mediaToDeployExist && sourceMediaSync.SyncState == MediaSync.State.deleted)
                {
                    // --------------------- TODO: Send this settings attached to the mediaSync instead of using an hard-coded value

                    // Check the settings to know whether to display an error message or simply continue ahead 
                    if (mediaDeploymentSettings.DeletingMediaIfNotFoundNoMessageError)
                    {
                        // Display an error message
                        result.errorMessage = "The media could not be found in the target site";
                        return result;
                    }
                    else
                    {
                        // In the case of a deleted media there is nothing to do since the media doesn't exist (couldn't be found)
                        // Assign an empty GUID in order to avoid the UI displaying an error message to the user
                        result.MediaGuid = Guid.Empty;
                        return result;
                    }

                }

                // If the media wasn't found and is a 'modified' command
                if (!mediaToDeployExist && sourceMediaSync.SyncState == MediaSync.State.modified)
                {
                    // --------------------- TODO: Add this setting in the UI (could be different for each target site) to allow the user to enable/disable this feature

                    // Check the settings to know whether to display an error message or simply continue ahead 
                    if (!mediaDeploymentSettings.ModifyingMediaIfNotFoundCreateMedia)
                    {
                        // Display an error message
                        result.errorMessage = "The media could not be found in the target site";
                        return result;
                    }
                    else
                    {
                        // We convert the 'modified' command into a 'created' command
                        sourceMediaSync.SyncState = MediaSync.State.created;
                    }

                }

                // Even if the media is marked as 'created' we need to check if it already exists, in order to avoid to create duplicate medias!
                if (mediaToDeployExist && sourceMediaSync.SyncState == MediaSync.State.created)
                {
                    // We convert the 'created' command into a 'modified' command in order to avoid duplicates
                    sourceMediaSync.SyncState = MediaSync.State.modified;
                }

                // If the media has to be deleted then bypass the following checks
                if (sourceMediaSync.SyncState != MediaSync.State.deleted)
                {

                    // Check whether the parent is mapped and exists
                    // The parameter parentMediaSync could be null. That means the parent node is the root node or recycle bin node.
                    if (dataJson.parentMediaSync != null && dataJson.parentMediaSync.TargetMediaGuid.ToString() != Guid.Empty.ToString())
                    {
                        // Mapped parent node
                        sourceMediaParentSync = JsonConvert.DeserializeObject<MediaSync>(JsonConvert.SerializeObject(dataJson.parentMediaSync));
                        if (sourceMediaParentSync.TargetMediaGuid != null && sourceMediaParentSync.TargetMediaGuid != Guid.Empty)
                            targetParentMedia = Services.MediaService.GetById(sourceMediaParentSync.TargetMediaGuid);
                        if (targetParentMedia == null)
                        {
                            result.errorMessage = "The parent node doesn't exist in the target site.";
                            return result;
                        }
                    }
                    else
                    {
                        // Check whether the Parent node is root or recycle bin
                        if (sourceMedia.ParentId != Constants.System.Root && sourceMedia.ParentId != Constants.System.RecycleBinMedia)
                        {
                            result.errorMessage = "The parent node is not mapped into the target site.";
                            return result;
                        }
                    }

                    // Check whether the node's Media Type exists
                    sourceMediaType = Services.ContentTypeService.GetMediaType(sourceMedia.ContentTypeAlias.ToString());

                    if (sourceMediaType == null)
                    {
                        result.errorMessage = string.Format("The media type '{0}' doesn't exist in the target site", sourceMedia.ContentTypeAlias.ToString());
                        return result;
                    }

                }

                // Media file system (required to access media files)
                MediaFileSystem mediaFileSystem = FileSystemProviderManager.Current.GetFileSystemProvider<MediaFileSystem>();

                var warningMessage = false;
                switch ((MediaSync.State)sourceMediaSync.SyncState)
                {
                    case MediaSync.State.unknown:
                        // Unexpected sync state !
                        warningMessage = true;
                        break;

                    case MediaSync.State.created:

                        // Create a new media node
                        var mediaParentId = 0;
                        if (sourceMedia.ParentId != Constants.System.Root && sourceMedia.ParentId != Constants.System.RecycleBinMedia)
                        {
                            mediaParentId = targetParentMedia.Id;
                        }
                        else
                        {
                            mediaParentId = (int)sourceMedia.ParentId;
                        }
                        IMedia mediaToCreate = Services.MediaService.CreateMedia(sourceMedia.Name.ToString(), mediaParentId, sourceMediaType.Alias);

                        // Check whether the media has been created properly
                        if (mediaToCreate == null || mediaToCreate.Key == null)
                        {
                            result.errorMessage = "Error creating new media in the target site";
                            return result;
                        }

                        // Assign the same Key
                        mediaToCreate.Key = new Guid(sourceMedia.Key.ToString());

                        // Get media files' data from the source
                        if (sourceMedia.MediaFiles != null)
                            sourceMediaFiles = sourceMedia.MediaFiles.ToObject<Dictionary<string, string>>();

                        // Update properties
                        foreach (var property in sourceMedia.Properties)
                        {
                            foreach (var propertyCollection in property.Children())
                            {
                                foreach (var propertyItem in propertyCollection.Children())
                                {
                                    // Check wheter the target to update contains the property
                                    //if (!mediaToUpdate.HasProperty(propertyItem.Alias.Value))
                                    if (!mediaToCreate.Properties.Contains(propertyItem.Alias.Value))
                                    {
                                        result.errorMessage = string.Format("The target media doesn't have any property with the alias'{0}'", propertyItem.Alias);
                                        return result;

                                    }
                                    // --------------------- TODO: Check wheter the property has the same type
                                    // for this we will use media .PropertyTypes() 

                                    // Update the property value
                                    ((Media)mediaToCreate).SetValue(propertyItem.Alias.Value, propertyItem.Value.Value);
                                }
                            }
                        }

                        if (sourceMedia.CreateDate != null)
                            mediaToCreate.CreateDate = Convert.ToDateTime(sourceMedia.CreateDate);

                        //if (sourceMedia.ExpireDate != null)
                        //    mediaToCreate.ExpireDate = Convert.ToDateTime(sourceMedia.ExpireDate);

                        //if (sourceMedia.Language != null)
                        //    mediaToCreate.Language = sourceMedia.Language;

                        mediaToCreate.Name = sourceMedia.Name;

                        //if (sourceMedia.ReleaseDate != null)
                        //    mediaToCreate.ReleaseDate = Convert.ToDateTime(sourceMedia.ReleaseDate);

                        mediaToCreate.SortOrder = sourceMedia.SortOrder;

                        if (sourceMedia.UpdateDate != null)
                            mediaToCreate.UpdateDate = Convert.ToDateTime(sourceMedia.UpdateDate);

                        // Check whether it is necesary to create media items for properties (upload control, cropimage)
                        // All these operations have to be performed inside a try/catch block in order to detect any error and delete the media we previously created!!
                        try
                        {
                            if (sourceMediaFiles != null)
                            {
                                // First of all, save the media in order to create the properties with Ids (since it is a new media). That is not necesary when
                                // updating an existing media!
                                Services.MediaService.Save(mediaToCreate);

                                // Look up for media properties and create media items
                                foreach (var property in sourceMedia.Properties)
                                {
                                    foreach (var propertyCollection in property.Children())
                                    {
                                        foreach (var propertyItem in propertyCollection.Children())
                                        {
                                            // Check wheter the property's datatype is 'UploadField'. 
                                            if (sourceMediaType.CompositionPropertyTypes.Where(x => x.Alias == propertyItem.Alias.Value && x.PropertyEditorAlias == Constants.PropertyEditors.UploadFieldAlias).Count() > 0)
                                            {
                                                if (sourceMediaFiles != null && sourceMediaFiles.ContainsKey(propertyItem.Value.Value))
                                                {
                                                    string fileData = sourceMediaFiles[propertyItem.Value.Value];

                                                    // Create the new media file
                                                    propertyItem.Value.Value = ContentAndMediaFileHelper.SaveMediaPropertyFile(mediaToCreate, propertyItem.Alias.Value, System.IO.Path.GetFileName(propertyItem.Value.Value), ContentAndMediaFileHelper.GetMediaFileStream(fileData));

                                                    // Update the property value
                                                    ((Media)mediaToCreate).SetValue(propertyItem.Alias.Value, propertyItem.Value.Value);
                                                }
                                                else
                                                {
                                                    // Update the property value
                                                    ((Media)mediaToCreate).SetValue(propertyItem.Alias.Value, string.Empty);
                                                }
                                            }

                                            // Check wheter the property's datatype is 'ImageCropper'. 
                                            if (sourceMediaType.CompositionPropertyTypes.Where(x => x.Alias == propertyItem.Alias.Value && x.PropertyEditorAlias == Constants.PropertyEditors.ImageCropperAlias).Count() > 0)
                                            {
                                                // Extract the file name from the Json data
                                                var propertyItemValueFilePath = ContentAndMediaFileHelper.ImageCropperGetImageFilePath(propertyItem.Value.Value.ToString());

                                                if (sourceMediaFiles != null && sourceMediaFiles.ContainsKey(propertyItemValueFilePath))
                                                {
                                                    string fileData = sourceMediaFiles[propertyItemValueFilePath];

                                                    // Create the new media file
                                                    var newMediaFilePath = ContentAndMediaFileHelper.SaveMediaPropertyFile(mediaToCreate, propertyItem.Alias.Value, System.IO.Path.GetFileName(propertyItemValueFilePath), ContentAndMediaFileHelper.GetMediaFileStream(fileData));
                                                    propertyItem.Value.Value = ContentAndMediaFileHelper.ImageCropperUpdateImageFilePath(propertyItem.Value.Value, newMediaFilePath);

                                                    // Update the property value
                                                    ((Media)mediaToCreate).SetValue(propertyItem.Alias.Value, propertyItem.Value.Value);
                                                }
                                                else
                                                {
                                                    // Update the property value
                                                    ((Media)mediaToCreate).SetValue(propertyItem.Alias.Value, string.Empty);
                                                }
                                            }

                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            Services.MediaService.Delete(mediaToCreate);
                            mediaToCreate = null;

                            // Throw the exception in order to let know the user about the error message
                            throw;
                        }

                        // Check whether the mediaToCreate exists. That means there were no errors with the media files.
                        if (mediaToCreate != null)
                        {
                            Services.MediaService.Save(mediaToCreate);

                            result.MediaGuid = mediaToCreate.Key;
                        }

                        break;

                    case MediaSync.State.modified:

                        //// If the media is not already mapped, resolve the mapping 
                        //if (sourceMediaSync.TargetMediaGuid == Guid.Empty)
                        //{
                        //    sourceMediaSync.TargetMediaGuid = resolveNonMappedMedia(sourceMedia);
                        //    if (sourceMediaSync.TargetMediaGuid == null || sourceMediaSync.TargetMediaGuid == Guid.Empty)
                        //    {
                        //        result.errorMessage = "Error resolving Non-Mapped media in the target site";
                        //        return result;
                        //    }
                        //}

                        // Load the media
                        var mediaToUpdate = Services.MediaService.GetById(sourceMediaSync.TargetMediaGuid);
                        if (mediaToUpdate == null)
                        {

                            result.errorMessage = "Couldn't find the media in the target site";
                            return result;
                        }

                        // Check whether the media type is the same and if it isn't then change it
                        if (mediaToUpdate.ContentType.Alias != sourceMedia.ContentTypeAlias.ToString())
                            mediaToUpdate.ChangeContentType(sourceMediaType);


                        // Check whether the parent node is the same or the media has been moved
                        if (targetParentMedia != null && mediaToUpdate.ParentId != targetParentMedia.Id)
                        {
                            if (targetParentMedia.Id != Constants.System.RecycleBinMedia)
                            {
                                Services.MediaService.Move(mediaToUpdate, targetParentMedia.Id);
                            }
                        }

                        // Check whether the node is trashed
                        if (sourceMedia.ParentId == Constants.System.RecycleBinMedia)
                        {
                            Services.MediaService.MoveToRecycleBin(mediaToUpdate);
                        }

                        // Get media files' data from the source
                        if (sourceMedia.MediaFiles != null)
                            sourceMediaFiles = sourceMedia.MediaFiles.ToObject<Dictionary<string, string>>();


                        // Update properties
                        foreach (var property in sourceMedia.Properties)
                        {
                            foreach (var propertyCollection in property.Children())
                            {
                                foreach (var propertyItem in propertyCollection.Children())
                                {
                                    // Check wheter the target to update contains the property
                                    //if (!mediaToUpdate.HasProperty(propertyItem.Alias.Value))
                                    if (!mediaToUpdate.Properties.Contains(propertyItem.Alias.Value))
                                    {
                                        result.errorMessage = string.Format("The target media doesn't have any property with the alias'{0}'", propertyItem.Alias);
                                        return result;

                                    }

                                    // --------------------- TODO: Check wheter the property has the same type
                                    // for this we will use media .PropertyTypes() 


                                    // Check wheter the property's datatype is 'UploadField' in order to manage the asociated media file
                                    if (sourceMediaType.CompositionPropertyTypes.Where(x => x.Alias == propertyItem.Alias.Value && x.PropertyEditorAlias == Constants.PropertyEditors.UploadFieldAlias).Count() > 0)
                                    {
                                        // Check whether that we have the media in the sourceMedia's collection of medias (sent within the media xml)
                                        if (sourceMediaFiles != null && sourceMediaFiles.ContainsKey(propertyItem.Value.Value))
                                        {
                                            // Check whether the property has currently a media item 
                                            var propertyCurrentValue = mediaToUpdate.Properties.First(x => x.Alias == propertyItem.Alias.Value).Value.ToString();

                                            // Get the media file media
                                            string newMediaFileData = sourceMediaFiles[propertyItem.Value.Value];

                                            // Check whether the media has changed in which case we need to update
                                            string currentMediaFileData = ContentAndMediaFileHelper.ReadMediaFile(propertyCurrentValue);
                                            if (!currentMediaFileData.Equals(newMediaFileData))
                                            {
                                                // Delete the media file if there is one
                                                ContentAndMediaFileHelper.DeleteMediaFile(propertyCurrentValue);

                                                // Create the new media file
                                                propertyItem.Value.Value = ContentAndMediaFileHelper.SaveMediaPropertyFile(mediaToUpdate, propertyItem.Alias.Value, System.IO.Path.GetFileName(propertyItem.Value.Value), ContentAndMediaFileHelper.GetMediaFileStream(newMediaFileData));
                                            }
                                            else
                                            {
                                                // Both media are equals, so no need to update, but need to keep the current MediaFilePath and not the mediaFilePath sent
                                                propertyItem.Value.Value = propertyCurrentValue;
                                            }
                                        }
                                        // Take in account the case in which the media item has been deleted 
                                        else if (string.IsNullOrWhiteSpace(propertyItem.Value.Value))
                                        {
                                            var propertyCurrentValue = mediaToUpdate.Properties.First(x => x.Alias == propertyItem.Alias.Value).Value.ToString();

                                            // Delete the media file if there is one
                                            ContentAndMediaFileHelper.DeleteMediaFile(propertyCurrentValue);

                                        }
                                    }

                                    // Check wheter the property's datatype is 'ImageCropper' in order to manage the asociated media file
                                    if (sourceMediaType.CompositionPropertyTypes.Where(x => x.Alias == propertyItem.Alias.Value && x.PropertyEditorAlias == Constants.PropertyEditors.ImageCropperAlias).Count() > 0)
                                    {
                                        // Extract the file name from the Json data
                                        var propertyItemValueFilePath = ContentAndMediaFileHelper.ImageCropperGetImageFilePath(propertyItem.Value.ToString());

                                        // Check whether that we have the media in the sourceMedia's collection of medias (sent within the media xml)
                                        if (!string.IsNullOrWhiteSpace(propertyItemValueFilePath) && sourceMediaFiles != null && sourceMediaFiles.ContainsKey(propertyItemValueFilePath))
                                        {
                                            // Check whether the property has currently a media item 
                                            var propertyCurrentValue = mediaToUpdate.Properties.First(x => x.Alias == propertyItem.Alias.Value).Value;

                                            // Extract the file name from the Json data
                                            var propertyCurrentValueFilePath = ContentAndMediaFileHelper.ImageCropperGetImageFilePath(propertyCurrentValue.ToString());

                                            // Get the media file media
                                            string newMediaFileData = sourceMediaFiles[propertyItemValueFilePath];

                                            // Check whether the media has changed in which case we need to update
                                            string currentMediaFileData = ContentAndMediaFileHelper.ReadMediaFile(propertyCurrentValueFilePath);
                                            if (!currentMediaFileData.Equals(newMediaFileData))
                                            {
                                                // Delete the media file if there is one
                                                ContentAndMediaFileHelper.DeleteMediaFile(propertyCurrentValueFilePath);

                                                // Create the new media file
                                                var newMediaFilePath = ContentAndMediaFileHelper.SaveMediaPropertyFile(mediaToUpdate, propertyItem.Alias.Value, System.IO.Path.GetFileName(propertyItemValueFilePath), ContentAndMediaFileHelper.GetMediaFileStream(newMediaFileData));
                                                propertyItem.Value.Value = ContentAndMediaFileHelper.ImageCropperUpdateImageFilePath(propertyItem.Value.Value, newMediaFilePath);
                                            }
                                            else
                                            {
                                                // Both media are equals, so no need to update, but need to keep the current MediaFilePath and not the mediaFilePath sent
                                                propertyItem.Value.Value = ContentAndMediaFileHelper.ImageCropperUpdateImageFilePath(propertyCurrentValue.ToString(), propertyCurrentValueFilePath);
                                            }
                                        }
                                        // Take in account the case in which the media item has been deleted 
                                        else if (string.IsNullOrWhiteSpace(propertyItem.Value.Value))
                                        {
                                            var propertyCurrentValue = mediaToUpdate.Properties.First(x => x.Alias == propertyItem.Alias.Value).Value.ToString();
                                            var propertyCurrentValueFilePath = ContentAndMediaFileHelper.ImageCropperGetImageFilePath(propertyCurrentValue);
                                            // Delete the media file if there is one
                                            ContentAndMediaFileHelper.DeleteMediaFile(propertyCurrentValueFilePath);
                                        }
                                    }

                                    // Update the property value
                                    ((Media)mediaToUpdate).SetValue(propertyItem.Alias.Value, propertyItem.Value.Value);
                                }
                            }
                        }

                        if (sourceMedia.CreateDate != null)
                            mediaToUpdate.CreateDate = Convert.ToDateTime(sourceMedia.CreateDate);

                        //if (sourceMedia.ExpireDate != null)
                        //    mediaToUpdate.ExpireDate = Convert.ToDateTime(sourceMedia.ExpireDate);

                        //if (sourceMedia.Language != null)
                        //    mediaToUpdate.Language = sourceMedia.Language;

                        mediaToUpdate.Name = sourceMedia.Name;

                        //if (sourceMedia.ReleaseDate != null)
                        //    mediaToUpdate.ReleaseDate = Convert.ToDateTime(sourceMedia.ReleaseDate);

                        mediaToUpdate.SortOrder = sourceMedia.SortOrder;

                        if (sourceMedia.UpdateDate != null)
                            mediaToUpdate.UpdateDate = Convert.ToDateTime(sourceMedia.UpdateDate);

                        Services.MediaService.Save(mediaToUpdate);

                        result.MediaGuid = mediaToUpdate.Key;

                        break;

                    case MediaSync.State.deleted:

                        //// If the media is not already mapped, resolve the mapping 
                        //if (sourceMediaSync.TargetMediaGuid == Guid.Empty)
                        //{
                        //    sourceMediaSync.TargetMediaGuid = resolveNonMappedMedia(sourceMedia);
                        //    if (sourceMediaSync.TargetMediaGuid == null || sourceMediaSync.TargetMediaGuid == Guid.Empty)
                        //    {
                        //        result.errorMessage = "Error resolving Non-Mapped media in the target site";
                        //        return result;
                        //    }
                        //}

                        // Load the media
                        var mediaToDelete = Services.MediaService.GetById(sourceMediaSync.TargetMediaGuid);
                        if (mediaToDelete == null)
                        {
                            result.errorMessage = "Couldn't find the mapped media in the target site";
                            return result;
                        }

                        // Delete the media
                        var mediaToDeleteKey = mediaToDelete.Key;
                        Services.MediaService.Delete(mediaToDelete);
                        result.MediaGuid = mediaToDeleteKey;

                        break;

                    case MediaSync.State.synced:
                        // Unexpected sync state !
                        warningMessage = true;
                        break;

                    default:
                        // Unexpected sync state !
                        warningMessage = true;
                        break;
                }

                if (warningMessage)
                    LogHelper.Warn<DeployApiController>("Current site (acting as a target site), for Media id = {0}, got unexpected sync state : {1}! ", () => dataJson.media.Id, () => sourceMediaSync.SyncState);

            }

            catch (Exception ex)
            {
                // Send the error message to the UI
                result.errorMessage = string.Format("Internal error <{0}>", ex.Message);
                return result;
            }

            return result;
        }

        /// <summary>
        /// Method called when a media is not found
        /// This method is virtual in order to allow the user to override it and use their own way to try to find a media (for instance, looking for a similar media using the media's name, id or any other property)
        /// </summary>
        /// <param name="media"></param>
        /// <returns></returns>
        public virtual Guid? resolveNotFoundMedia(dynamic media)
        {
            Guid? result = null;

            // TO BE customized

            return result;
        }

        public IMedia GetMedia(Guid mediaGuid)
        {
            IMedia result = null;

            var media = Services.MediaService.GetById(mediaGuid);
            if (media != null)
                result = media;

            return result;
        }

        [System.Web.Http.HttpPost]
        [System.Web.Http.HttpOptions]
        public bool SaveMedia(IMedia media)
        {
            if (media != null)
            {
                return true;
                //Services.MediaService.Save(media);
            }
            return false;
        }

        public bool DeleteMedia(Guid mediaGuid)
        {
            var result = false;
            var media = Services.MediaService.GetById(mediaGuid);
            if (media != null)
            {
                Services.MediaService.MoveToRecycleBin(media);
                result = true;
            }
            return result;
        }

        #endregion

    }
}
