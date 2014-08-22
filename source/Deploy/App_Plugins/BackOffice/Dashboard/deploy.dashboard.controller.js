'use strict';
(function () {

    //Main controller
    function DeployDashboardController($log, $rootScope, $scope, $routeParams, $injector, notificationsService, contentEditingHelper, formHelper, umbPropEditorHelper, serverValidationManager, navigationService, DeployResource) {

        $scope.actionInProgress = false;

        // Set up the standard data type props
        $scope.properties = {
            enabledEditor: {
                alias: "enabledEditor",
                description: "",
                label: "Enabled"
            },
            securityKeyEditor: {
                alias: "securityKeyEditor",
                description: "",
                label: "Security Key"
            },
        };

        // Get current site Id
        $scope.currentSiteId = -1;
        if ($routeParams.id) {
            $scope.currentSiteId = $routeParams.id;
        }

        // Get current site's data
        DeployResource.getCurrentSite($scope.currentSiteId)
            .then(
                function (response) {
                    $scope.currentSiteModel = response.data;
                    $scope.loaded = true;
                },
                function () {
                    notificationsService.error("Error", "Error loading data");
                }
            );

        // Save
        $scope.save = function () {

            if (formHelper.submitForm({ scope: $scope })) {

                $scope.actionInProgress = true;

                // Save data
                DeployResource.saveCurrentSite($scope.currentSiteModel).then(
                    function (result) {

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
            };
        };

    };

    //register the controller
    angular.module("umbraco").controller('Deploy.Dashboard.DeployDashboardController', DeployDashboardController);

})();



