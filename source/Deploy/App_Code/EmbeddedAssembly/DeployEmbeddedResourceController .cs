using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

using Umbraco.Core.Logging;

namespace Deploy.EmbeddedAssembly
{
    // Get embedded resources files (.html, .js, .css, ...) 
    public class DeployEmbeddedResourceController : Controller
    {

        public FileStreamResult GetRootResource(string resource)
        {
            return GetResource(DeployEmbeddedResourcePaths.DeployRoot, resource);
        }

        public FileStreamResult GetDashboardResource(string resource)
        {
            return GetResource(DeployEmbeddedResourcePaths.DeployDashboard, resource);
        }

        public FileStreamResult GetTreeResource(string resource)
        {
            return GetResource(DeployEmbeddedResourcePaths.DeployTree, resource);
        }

        private FileStreamResult GetResource(string url, string resource)
        {
            try
            {
                LogHelper.Info(typeof(DeployEmbeddedResourceController), string.Format("Getting the resource: {0}{1}", url, resource));

                // get this assembly
                Assembly assembly = typeof(DeployEmbeddedResourceController).Assembly;

                // if resource can be found
                string resourceName = string.Format("{0}.{1}{2}{3}", assembly.GetName().Name, "App_Plugins", url.Replace("/", "."), resource);
                LogHelper.Info(typeof(DeployEmbeddedResourceController), string.Format("Getting the resource: {0}", resourceName));
                if (Assembly.GetExecutingAssembly().GetManifestResourceNames().Any(x => x.Equals(resourceName, StringComparison.InvariantCultureIgnoreCase)))
                {
                    return new FileStreamResult(assembly.GetManifestResourceStream(resourceName), this.GetMimeType(resourceName));
                }
                else
                {
                    LogHelper.Warn(typeof(DeployEmbeddedResourceController), string.Format("Couldn't get the resource: {0}{1}", url, resource));
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warn(typeof(DeployEmbeddedResourceController), string.Format("Couldn't get the resource: {0}{1} {2}{3}", url, resource, Environment.NewLine, ex.Message));
            }

            return null;
        }

        private string GetMimeType(string resource)
        {
            switch (Path.GetExtension(resource))
            {
                case ".html": return "text/html";
                case ".css": return "text/css";
                case ".js": return "text/javascript";
                case ".png": return "image/png";
                default: return "text";
            }
        }

    }
}