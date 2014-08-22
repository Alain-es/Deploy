'use strict';
(function () {

    //Main controller
    function DeploySettingsController($log, $rootScope, $scope, $routeParams, $injector, notificationsService, contentEditingHelper, formHelper, umbPropEditorHelper, serverValidationManager, navigationService, DeployResource) {

        $scope.actionInProgress = false;

        // Tabs
        $scope.tabs = [
                { id: 1, label: "Settings" },
        ];

        // Set up the standard data type props
        $scope.properties = {
            siteNameEditor: {
                alias: "siteNameEditor",
                description: "",
                label: "Site Name"
            },
            urlEditor: {
                alias: "urlEditor",
                description: "Target site url (for example http://www.mydomain.com)",
                label: "Url"
            },
            securityKeyEditor: {
                alias: "securityKeyEditor",
                description: "",
                label: "Security Key"
            },
        };

        // Get targetSite's Id
        $scope.targetSiteId = -1;
        if ($routeParams.id) {
            $scope.targetSiteId = $routeParams.id;
        }

        // Get TargetSite's data
        DeployResource.getTargetSite($scope.targetSiteId)
            .then(
                function (response) {
                    $scope.settingsModel = response.data;
                    //$log.log($scope.settingsModel);

                    $scope.loaded = true;
                },
                function () {
                    notificationsService.error("Error", "Error loading data");
                }
            );

        // Save
        $scope.save = function () {

            //if (this.settingsForm.$valid) {
            //    alert($scope.settingsModel.SiteName);

            if (formHelper.submitForm({ scope: $scope })) {

                $scope.actionInProgress = true;

                DeployResource.getTargetSite($scope.targetSiteId).then(
                    function (result) {

                        var targetSiteExists = (result.data.Id == $scope.targetSiteId);

                        // Before saving it is necesary to check that the TargetSite still exist (It could have been deleted by the user, but the editor could remain opened)
                        if (targetSiteExists) {

                            // Check whether the url ends with a trailing slash
                            var url = $scope.settingsModel.Url;
                            if (url != null && url != undefined && $.trim(url).length > 0 && url.substr(-1) != '/') {
                                $scope.settingsModel.Url += '/';
                            }

                            // Save data
                            DeployResource.saveTargetSite($scope.settingsModel).then(
                                function (result) {

                                    // Refresh the tree 
                                    navigationService.syncTree({ tree: "DeployTree", path: ["-1", "0"], forceReload: true, activate: false });

                                    // Reset form to avoid the confirmation message
                                    formHelper.resetForm({ scope: $scope });

                                    $scope.actionInProgress = false;

                                    notificationsService.success("Data saved successfully", "");
                                },
                                function (error) {
                                    $scope.actionInProgress = false;

                                    var errorMessage = "";
                                    if (error.data && error.data.Message) {
                                        errorMessage = error.data.Message;
                                    }
                                    notificationsService.error("Error while saving data", errorMessage);
                                }
                             );
                        }
                        else {
                            $scope.actionInProgress = false;
                            notificationsService.error("Error", "Couldn't save data because the target site doesn't exist");
                        }

                    });
            };
        };

    };

    //register the controller
    angular.module("umbraco").controller('Deploy.DeployTree.DeploySettingsController', DeploySettingsController);

})();



