'use strict';
(function () {

    // Create controller
    function DeployDeleteController($scope, $routeParams, $http, treeService, navigationService, DeployResource) {

        // Used for debug purposes
        $scope.showDebug = Umbraco.Sys.ServerVariables.isDebuggingEnabled;

        $scope.delete = function () {

            // Mark it for deletion (used in the UI)
            $scope.currentNode.loading = true;

            DeployResource.getTargetSiteDeleted($scope.currentNode.id).then(function () {
                $scope.currentNode.loading = false;

                // Delete the node
                var node = treeService.getTreeRoot($scope.currentNode);
                treeService.removeNode($scope.currentNode);

                navigationService.hideMenu();
            });

        };

        $scope.cancel = function () {
            navigationService.hideDialog();
        };
    }

    // Register controller
    angular.module("umbraco").controller('Deploy.DeployTree.DeployDeleteController', DeployDeleteController);

})();