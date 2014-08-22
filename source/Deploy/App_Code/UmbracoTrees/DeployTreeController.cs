using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Formatting;
using System.Web;

using Umbraco.Core;
using Umbraco.Web.Models.Trees;
using Umbraco.Web.Mvc;
using Umbraco.Web.Trees;
using umbraco.interfaces;
using umbraco.BusinessLogic.Actions;

using Deploy.Controllers.Api;
using Deploy.Models;

namespace Deploy.UmbracoTrees
{
    // Tree for the new application

    // Tree Attribute params:
    //    appAlias - The application alias the tree is associated with
    //    alias - The alias for the tree
    //    title - The friendly title for the tree
    //    iconClosed [Optional, Default = ".sprTreeFolder"] - The icon to use for closed tree nodes
    //    iconOpen [Optional, Default = ".sprTreeFolder_o"] - The icon to use for open tree nodes
    [Tree("Deploy", "DeployTree", "Deploy")]
    [PluginController("Deploy")]
    public class DeployTreeController : TreeController
    {
        protected override MenuItemCollection GetMenuForNode(string id, FormDataCollection queryStrings)
        {
            var menuItems = new MenuItemCollection();

            // Check whether we're rendering the root node's children
            if (id == Constants.System.Root.ToInvariantString())
            {
                //menu.DefaultMenuAlias = ActionNew.Instance.Alias;
                menuItems.Items.Add<ActionNew>("Create");
            }
            else
            {
                // Create new menu item "Settings"
                var m = new MenuItem("settings", "Settings");
                m.Icon = "settings-alt";
                m.NavigateToRoute(string.Format("/Deploy/DeployTree/settings/{0}", id));
                menuItems.Items.Add(m);

                // Create new menu item "Delete"
                m = new MenuItem("delete", "Delete");
                m.Icon = "delete";
                m.SeperatorBefore = true;
                menuItems.Items.Add(m);
            }

            return menuItems;
        }

        protected override TreeNodeCollection GetTreeNodes(string id, FormDataCollection queryStrings)
        {
            var deployApi = new DeployApiController();
            var treeNodes = new TreeNodeCollection();

            // Check whether we're rendering the root node's children
            if (id == Constants.System.Root.ToInvariantString())
            {
                // Get all target sites
                foreach (var targetSite in deployApi.GetTargetSites())
                {
                    // Add the target site node to the tree
                    treeNodes.Add(CreateTreeNode(targetSite.Id.ToString(), id, queryStrings, targetSite.SiteName, "icon-umb-translation", true));
                }
            }
            else
            {
                // Pending changes are now displayed in the list view, so there is no need to display any pending changes in the tree.
                // Nevertheless, we keep this source code in case we need it in the future.

                //// Get target site id
                //int targetSiteId = 0;
                //if (int.TryParse(id, out targetSiteId))
                //{
                //    // Get all contents with pending changes
                //    foreach (var contentSync in deployApi.GetAllContentSyncsByTargetSite(targetSiteId, true))
                //    {
                //        var icon = string.Empty;
                //        switch (contentSync.SyncState)
                //        {
                //            case Deploy.Models.DatabasePocos.ContentSync.State.unknown:
                //                icon = "icon-document-dashed-line";
                //                break;
                //            case Deploy.Models.DatabasePocos.ContentSync.State.created:
                //                icon = "icon-add";
                //                break;
                //            case Deploy.Models.DatabasePocos.ContentSync.State.modified:
                //                icon = "icon-edit";
                //                break;
                //            case Deploy.Models.DatabasePocos.ContentSync.State.deleted:
                //                icon = "icon-delete";  // "icon-trash";
                //                break;
                //            case Deploy.Models.DatabasePocos.ContentSync.State.synced:
                //            default:
                //                icon = "icon-document";
                //                break;
                //        }
                //        treeNodes.Add(CreateTreeNode(contentSync.Id.ToString(), id, queryStrings, contentSync.SyncState.ToString(), icon));
                //    }
                //}
            }
            return treeNodes;
        }
    }
}