using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

using umbraco;
using Umbraco.Core;
using Umbraco.Core.Models;

namespace Deploy.Helpers
{
    public class MediaHelper
    {
        /// <summary>
        /// The purpose of this method is to double check that a media has been really trashed.
        /// </summary>
        /// <returns></returns>
        public static bool IsMediaTrashed(int mediaId)
        {
            return ApplicationContext.Current.Services.MediaService.GetById(mediaId).Trashed;
        }

        /// <summary>
        /// The purpose of this method is to double check that a media has been really deleted (doesn't exist in the database)
        /// </summary>
        /// <returns></returns>
        public static bool IsMediaDeleted(int mediaId)
        {
            return ApplicationContext.Current.Services.MediaService.GetById(mediaId) == null;
        }
    }
}