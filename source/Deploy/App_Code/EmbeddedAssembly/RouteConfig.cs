using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

using Umbraco.Core.Logging;

namespace Deploy.EmbeddedAssembly
{

    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {

            const string pluginBasePath = "App_Plugins/Deploy";
            string url = string.Empty;

            url = string.Format("{0}{1}{2}", pluginBasePath, DeployEmbeddedResourcePaths.DeployRoot, "{resource}");
            RouteTable.Routes.MapRoute(
                name: "DeployRoot",
                url: url,
                defaults: new
                {
                    controller = "DeployEmbeddedResource",
                    action = "GetRootResource"
                }
            );
            LogHelper.Info(typeof(RouteConfig), string.Format("Registering route : {0}", url).Replace("{", "{{").Replace("}", "}}"));

            url = string.Format("{0}{1}{2}", pluginBasePath, DeployEmbeddedResourcePaths.DeployDashboard, "{resource}");
            RouteTable.Routes.MapRoute(
                name: "DeployDashboard",
                url: url,
                defaults: new
                {
                    controller = "DeployEmbeddedResource",
                    action = "GetDashboardResource"
                }
            );
            LogHelper.Info(typeof(RouteConfig), string.Format("Registering route : {0}", url).Replace("{", "{{").Replace("}", "}}"));

            url = string.Format("{0}{1}{2}", pluginBasePath, DeployEmbeddedResourcePaths.DeployTree, "{resource}");
            RouteTable.Routes.MapRoute(
                name: "DeployTree",
                url: url,
                defaults: new
                {
                    controller = "DeployEmbeddedResource",
                    action = "GetTreeResource"
                }
            );
            LogHelper.Info(typeof(RouteConfig), string.Format("Registering route : {0}", url).Replace("{", "{{").Replace("}", "}}"));



        }
    }

}