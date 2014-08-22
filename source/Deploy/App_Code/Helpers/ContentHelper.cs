using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.IO;

using Umbraco.Core;
using Umbraco.Core.Models;
using Umbraco.Core.Logging;
using Umbraco.Web;
using Umbraco.Web.Mvc;
using Umbraco.Web.Models;
using Umbraco.Web.Controllers;
using Umbraco.Web.Routing;
using Umbraco.Web.WebApi;

namespace Deploy.Helpers
{
    public static class ContentHelper
    {

        /// <summary>
        /// The purpose of this method is to double check that a content has been really trashed.
        /// </summary>
        /// <returns></returns>
        public static bool IsContentTrashed(int contentId)
        {
            return ApplicationContext.Current.Services.ContentService.GetById(contentId).Trashed;
        }

        /// <summary>
        /// The purpose of this method is to double check that a content has been really deleted (doesn't exist in the database)
        /// </summary>
        /// <returns></returns>
        public static bool IsContentDeleted(int contentId)
        {
            return ApplicationContext.Current.Services.ContentService.GetById(contentId) == null;
        }

    }

}




