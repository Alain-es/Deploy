using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

using umbraco.businesslogic;
using umbraco.interfaces;

namespace Deploy.UmbracoSections
{
    // Create the new application (section) in the backoffice
    //Application Attribute Params:
    //    alias - The alias for the application
    //    name - The friendly name for the application
    //    icon - The icon css class / image filename to use in the application tray
    //    sortOrder [Optional] - The order in which the icon should appear in the application tray
    [Application("Deploy", "Deploy", "icon-shuffle", 15)]
    public class DeploySection : IApplication
    {
    }

}