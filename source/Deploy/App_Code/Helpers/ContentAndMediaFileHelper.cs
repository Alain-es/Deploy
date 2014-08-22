using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
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

using Newtonsoft.Json.Linq;

namespace Deploy.Helpers
{
    public class ContentAndMediaFileHelper
    {

        public static void DeleteMediaFile(string mediaFilePath)
        {
            DeleteMediaFile(mediaFilePath, true, true);
        }

        public static void DeleteMediaFile(string mediaFilePath, bool deleteThumbnails)
        {
            DeleteMediaFile(mediaFilePath, deleteThumbnails, true);
        }

        public static void DeleteMediaFile(string mediaFilePath, bool deleteThumbnails, bool deleteDirectory)
        {
            if (string.IsNullOrWhiteSpace(mediaFilePath))
                return;

            MediaFileSystem mediaFileSystem = FileSystemProviderManager.Current.GetFileSystemProvider<MediaFileSystem>();

            var mediaRelativeFilePath = mediaFileSystem.GetRelativePath(mediaFilePath);
            mediaFileSystem.DeleteFile(mediaRelativeFilePath, deleteThumbnails);

            if (deleteDirectory)
            {
                var parentDirectory = System.IO.Path.GetDirectoryName(mediaRelativeFilePath);
                if (parentDirectory != mediaFileSystem.GetRelativePath("/"))
                {
                    mediaFileSystem.DeleteDirectory(parentDirectory, false);
                }
            }
        }

        public static string ReadMediaFile(string mediaFilePath, bool encodeBase64 = true)
        {
            var result = string.Empty;

            MediaFileSystem mediaFileSystem = FileSystemProviderManager.Current.GetFileSystemProvider<MediaFileSystem>();
            if (!string.IsNullOrWhiteSpace(mediaFilePath) && mediaFileSystem.FileExists(IOHelper.MapPath(mediaFilePath)))
            {
                using (var stream = mediaFileSystem.OpenFile(IOHelper.MapPath(mediaFilePath)))
                {
                    byte[] bytes = new byte[stream.Length];
                    stream.Position = 0;
                    stream.Read(bytes, 0, (int)stream.Length);
                    if (encodeBase64)
                    {
                        result = Convert.ToBase64String(bytes);
                    }
                    else
                    {
                        result = Encoding.UTF8.GetString(bytes);
                    }

                }
            }
            return result;
        }

        public static void WriteMediaFile(string mediaFilePath, string mediaFileData, bool encodedBase64 = true)
        {
            MediaFileSystem mediaFileSystem = FileSystemProviderManager.Current.GetFileSystemProvider<MediaFileSystem>();
            if (!string.IsNullOrWhiteSpace(mediaFilePath))
            {
                byte[] fileData = encodedBase64 == false ? System.Text.Encoding.UTF8.GetBytes(mediaFileData) : Convert.FromBase64String(mediaFileData);
                using (var stream = new MemoryStream(fileData))
                {
                    mediaFileSystem.AddFile(mediaFilePath, stream, true);
                }
            }
        }

        public static Stream GetMediaFileStream(string mediaFileDataSerialized, bool encodedBase64 = true)
        {
            byte[] fileData = encodedBase64 == false ? System.Text.Encoding.UTF8.GetBytes(mediaFileDataSerialized) : Convert.FromBase64String(mediaFileDataSerialized);
            return new MemoryStream(fileData);
        }


        public static void SaveContentPropertyFile(int contentId, string propertyTypeAlias, string fileName, Stream fileData)
        {
            var content = ApplicationContext.Current.Services.ContentService.GetById(contentId);
            SaveContentPropertyFile(content, propertyTypeAlias, fileName, fileData);
        }

        public static string SaveContentPropertyFile(IContent content, string propertyTypeAlias, string fileName, Stream fileData)
        {
            Umbraco.Core.Models.ContentExtensions.SetValue(content, propertyTypeAlias, fileName, fileData);

            // Retrieve the new image file's path
            var property = content.Properties.First(x => x.Alias == propertyTypeAlias);
            return property.Value.ToString();
        }

        public static void SaveMediaPropertyFile(int mediaId, string propertyTypeAlias, string fileName, Stream fileData)
        {
            var media = ApplicationContext.Current.Services.MediaService.GetById(mediaId);
            SaveMediaPropertyFile(media, propertyTypeAlias, fileName, fileData);
        }

        public static string SaveMediaPropertyFile(IMedia media, string propertyTypeAlias, string fileName, Stream fileData)
        {
            Umbraco.Core.Models.ContentExtensions.SetValue(media, propertyTypeAlias, fileName, fileData);

            // Retrieve the new image file's path
            var property = media.Properties.First(x => x.Alias == propertyTypeAlias);
            return property.Value.ToString();
        }


        public static string ImageCropperGetImageFilePath(string propertyValue)
        {
            var result = string.Empty;

            if (!string.IsNullOrWhiteSpace(propertyValue))
            {
                JObject propertyValueJson = null;
                // Parse the property's value into a Json Object
                try
                {
                    propertyValueJson = JObject.Parse(propertyValue);
                }
                catch (Exception ex)
                {
                    LogHelper.WarnWithException<ContentAndMediaFileHelper>("Error parsing ImageCropper property value to a JObject", ex);
                }

                // Get the media file path
                if (propertyValueJson != null && propertyValueJson["src"] != null)
                {
                    // Media file 
                    result = propertyValueJson["src"].Value<string>();
                }
            }

            return result;
        }

        public static string ImageCropperUpdateImageFilePath(string propertyValue, string newImageFilePath)
        {
            var result = propertyValue;

            if (!string.IsNullOrWhiteSpace(propertyValue))
            {
                JObject propertyValueJson = null;
                // Parse the property's value into a Json Object
                try
                {
                    propertyValueJson = JObject.Parse(propertyValue);
                }
                catch (Exception ex)
                {
                    LogHelper.WarnWithException<ContentAndMediaFileHelper>("Error parsing ImageCropper property value to a JObject", ex);
                }

                // Get the media file path
                if (propertyValueJson != null && propertyValueJson["src"] != null)
                {
                    // Media file 
                    propertyValueJson["src"] = newImageFilePath;
                    result = propertyValueJson.ToString();
                }
            }

            return result;
        }


    }
}