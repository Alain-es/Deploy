using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Web;

using System.Web.Http.Filters;
using System.Web.Http.Controllers;
using System.Net.Http;

using umbraco;
using Umbraco.Core;
using Umbraco.Core.Strings;

using Deploy.Helpers;
using Deploy.Controllers.Api;

namespace Deploy.ActionFilters
{
    public class AllowCrossSiteJsonAttribute : ActionFilterAttribute
    {
        public const string AuthorizationToken = "AuthorizationToken";
        public const string HeaderOrigin = "origin";

        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
        {
            if (actionExecutedContext.Response != null)
            {
                var headerOrigin = string.Empty;
                if (actionExecutedContext.Request.Headers.Contains(HeaderOrigin))
                {
                    headerOrigin = actionExecutedContext.Request.Headers.GetValues(HeaderOrigin).FirstOrDefault();
                }
                if (!string.IsNullOrWhiteSpace(headerOrigin))
                {
                    //actionExecutedContext.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                    actionExecutedContext.Response.Headers.Add("Access-Control-Allow-Origin", headerOrigin);
                    actionExecutedContext.Response.Headers.Add("Access-Control-Allow-Headers", "content-type, " + AuthorizationToken);
                    //actionExecutedContext.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, PUT, DELETE, OPTIONS");
                }
            }
            base.OnActionExecuted(actionExecutedContext);
        }

        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            if (actionContext.Request != null && actionContext.Request.Method != HttpMethod.Options)
            {
                // Retrieve current site settings
                var deployApi = new DeployApiController();
                var currentSite = deployApi.GetCurrentSite();
                // First of all check whether deploy is enabled for the current site
                if (currentSite == null || !currentSite.Enabled)
                {
                    actionContext.Response = new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("Remote deployment is not enabled for this site.")
                    };
                    return;

                }
                // Check whether there is a security key
                bool authorized = false;
                if (currentSite.SecurityKey != null && !string.IsNullOrWhiteSpace(currentSite.SecurityKey))
                {

                    // If there is a security key, then checks that the origin header has been encripted using the security key
                    // Check wheter the origin passed by querystring matches 
                    string headerOrigin = string.Empty;
                    if (actionContext.Request.Headers.Contains(HeaderOrigin))
                    {
                        headerOrigin = actionContext.Request.Headers.GetValues(HeaderOrigin).FirstOrDefault();
                        // Extract Host name
                        if (!string.IsNullOrWhiteSpace(headerOrigin))
                        {
                            var originUri = new Uri(headerOrigin);
                            if (originUri != null)
                                headerOrigin = originUri.DnsSafeHost;
                        }
                    }
                    string authorizationToken = string.Empty;
                    if (actionContext.Request.Headers.Contains(AuthorizationToken))
                    {
                        authorizationToken = actionContext.Request.Headers.GetValues(AuthorizationToken).FirstOrDefault();
                    }
                    if (headerOrigin != null && !string.IsNullOrWhiteSpace(headerOrigin)
                        && authorizationToken != null && !string.IsNullOrWhiteSpace(authorizationToken)
                        && CryptographyHelper.Decrypt(authorizationToken.FromUrlBase64(), currentSite.SecurityKey) == headerOrigin)
                    {
                        authorized = true;
                    }
                }
                // If the request is not authorized, change the response to let know the caller
                if (!authorized)
                {
                    actionContext.Response = new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("Missing Authorization-Token")
                    };
                    return;
                }
            }
            base.OnActionExecuting(actionContext);
        }
    }
}
