using System;
using System.Web;
using System.Web.Mvc;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;

using umbraco;
using Umbraco.Core;
using Umbraco.Core.Dynamics;
using Umbraco.Core.Models;
using Umbraco.Core.Logging;
using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.DatabaseAnnotations;
using Umbraco.Core.Persistence.DatabaseModelDefinitions;
using umbraco.BusinessLogic.Actions;
using umbraco.BusinessLogic;
using Umbraco.Web;
using Umbraco.Web.Editors;
using Umbraco.Web.Mvc;
using Umbraco.Web.WebApi;
using Umbraco.Web.Models.ContentEditing;

using AutoMapper;

using Deploy.Models;
using Deploy.Models.DatabasePocos;
using Deploy.Helpers;

namespace Deploy.Controllers.Api
{
    [PluginController("Deploy")]
    [IsBackOffice]
    public class DeployApiRemoteSecurityController : UmbracoAuthorizedJsonController
    {
        // The security Key is generated on the server side using an UmbracoAuthorizedJson Controller for security reasons
        [HttpGet]
        public string GetSecurityKey(int targetSiteId)
        {
            string result = string.Empty;

            // Retrieve the SecurityKey from the site settings
            var deployApi = new DeployApiController();
            var targetSite = deployApi.GetTargetSite(targetSiteId);
            if (targetSite != null && targetSite.SecurityKey != null && !string.IsNullOrWhiteSpace(targetSite.SecurityKey))
            {
                result = CryptographyHelper.Encrypt(HttpContext.Current.Request.Url.DnsSafeHost, targetSite.SecurityKey).ToUrlBase64();
            }
            return result;
        }

    }
}




