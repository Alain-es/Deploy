'use strict';
(function () {

    // Create controller
    function DeployCreateController($log, $scope, $routeParams, $http, $location, treeService, navigationService, eventsService, appState, formHelper, DeployResource) {

        $scope.hasError = false;
        $scope.actionInProgress = false;

        $scope.createTargetSite = function () {

            $scope.actionInProgress = true;
            $scope.hasError = true;

            if (formHelper.submitForm({ scope: $scope })) {

                $scope.hasError = false;

                // Check whether the TargetSite name is empty
                if ($scope.targetSiteName != null && $scope.targetSiteName != undefined && $.trim($scope.targetSiteName).length > 0) {

                    // Create the new target site
                    var targetSite = {
                        SiteName: $scope.targetSiteName,
                        Url: "",
                        SecurityKey: "",
                    };

                    DeployResource.saveTargetSite(targetSite).then(function (result) {

                        var TargetSiteid = result.data;
                        //$log.log(TargetSiteid);

                        // Hide the dialog
                        navigationService.hideDialog();

                        // Refresh the tree 
                        navigationService.syncTree({ tree: "DeployTree", path: ["-1", "0"], forceReload: true, activate: false });

                        // Reset form to avoid the confirmation message
                        formHelper.resetForm({ scope: $scope });

                        // Redirect to the setting page
                        $location.path("/Deploy/DeployTree/settings/" + TargetSiteid);

                    });

                    $scope.actionInProgress = false;
                }
                else {
                    // If the TargetSite name is empty then display an error message
                    $scope.hasError = true;
                    $scope.actionInProgress = false;
                }
            }

        }

        $scope.cancel = function () {
            navigationService.hideDialog();
        };

    };

    // Register controller
    angular.module("umbraco").controller('Deploy.DeployTree.DeployCreateController', DeployCreateController);

})();