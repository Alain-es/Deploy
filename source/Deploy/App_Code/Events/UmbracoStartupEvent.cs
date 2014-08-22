using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

using umbraco.cms.presentation;
using Umbraco.Core;
using umbraco.BusinessLogic;
using umbraco.cms.businesslogic;
using umbraco.cms.businesslogic.web;
using Umbraco.Core.Persistence;
using Umbraco.Core.Logging;

using Deploy.Models.DatabasePocos;
using Deploy.Controllers.Api;
using Deploy.Helpers;
using Deploy.EmbeddedAssembly;

namespace Deploy.Installer
{
    public class UmbracoStartupEvent : ApplicationEventHandler
    {
        protected override void ApplicationStarted(UmbracoApplicationBase umbracoApplication, ApplicationContext applicationContext)
        {
            base.ApplicationStarted(umbracoApplication, applicationContext);

            // Register routes for embedded files
            RouteConfig.RegisterRoutes(RouteTable.Routes);

            // Create section's language Keys
            LanguageHelper.CheckAndInstallLanguageActions();

            // Create DB tables (if they don't exist)
            var db = applicationContext.DatabaseContext.Database;
            db.CreateTable<ContentSync>(false);
            db.CreateTable<MediaSync>(false);
            db.CreateTable<CurrentSite>(false);
            db.CreateTable<TargetSite>(false);

            // Create record for current site settings if none exists
            var deployApi = new DeployApiController();
            if (deployApi.GetCurrentSite() == null)
            {
                deployApi.SaveCurrentSite(new CurrentSite { SecurityKey = "Akd$v2%Iz19" });
            }

            //// Create a new target site if none exists (If there is no target site, no changes will be recorded)
            //if (deployApi.GetTargetSites().Count() <= 0)
            //{
            //    //LogHelper.Info<ContentEvents>("Default target site created.");
            //    deployApi.SaveTargetSite(new TargetSite { SiteName = "Default Target Site" });
            //}

            // Attach Content events (copied, saved, deleted, ...)
            ContentEvents contentEvents = new ContentEvents();
            contentEvents.AttachEvents();

            // Attach Media events (copied, saved, deleted, ...)
            MediaEvents mediaEvents = new MediaEvents();
            mediaEvents.AttachEvents();

        }
    }
}