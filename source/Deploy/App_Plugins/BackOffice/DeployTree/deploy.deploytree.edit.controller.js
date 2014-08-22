'use strict';
(function () {

    // ---------------------------------------------- Main controller ----------------------------------------------
    function DeployEditController($rootScope, $scope, $routeParams, $injector, notificationsService, iconHelper, dialogService, DeployResource) {

        // Initializations
        $scope.targeSiteAccessible = true;

        // Tabs
        $scope.tabs = [
                { id: 1, label: "Content deployement" },
                { id: 2, label: "Media deployement" },
        ];

        // Get current targetSite's Id
        $scope.targetSiteId = -1;
        if ($routeParams.id) {
            $scope.targetSiteId = $routeParams.id;
        }

        // Get target site's data 
        DeployResource.getTargetSite($scope.targetSiteId).then(
                function (response) {
                    $scope.targetSiteModel = response.data;
                },
                function () {
                    notificationsService.error("Error", "Error loading data");
                }
            );

        // Check whether the selected target site is accesible
        DeployResource.remoteTestAccess($scope.targetSiteId).then(
            function (response) {
                $scope.targeSiteAccessible = response.data;
            },
            function (error) {
                $scope.targeSiteAccessible = false;
            }
        );


    }



    // ---------------------------------------------- Content controller ----------------------------------------------
    function DeployEditContentController($q, $rootScope, $scope, $routeParams, $injector, $log, notificationsService, iconHelper, dialogService, contentResource, DeployResource) {

        $scope.entityType = "content";
        $scope.actionInProgress = false;

        $scope.listViewResultSet = {
            totalPages: 0,
            items: []
        };

        $scope.options = {
            pageSize: 50,
            pageNumber: 1,
            filter: '',
            orderBy: 'SortOrder',
            orderDirection: "asc"
        };

        $scope.targetSiteId = -1;
        if ($routeParams.id) {
            $scope.targetSiteId = $routeParams.id;
        }

        $scope.next = function () {
            if ($scope.options.pageNumber < $scope.listViewResultSet.totalPages) {
                $scope.options.pageNumber++;
                $scope.reloadView($scope.targetSiteId);
            }
        };

        $scope.goToPage = function (pageNumber) {
            $scope.options.pageNumber = pageNumber + 1;
            $scope.reloadView($scope.targetSiteId);
        };

        $scope.sort = function (field) {
            $scope.options.orderBy = field;
            if ($scope.options.orderDirection === "desc") {
                $scope.options.orderDirection = "asc";
            } else {
                $scope.options.orderDirection = "desc";
            }
            $scope.reloadView($scope.targetSiteId);
        };

        $scope.prev = function () {
            if ($scope.options.pageNumber > 1) {
                $scope.options.pageNumber--;
                $scope.reloadView($scope.targetSiteId);
            }
        };

        /*Loads the search results, based on parameters set in prev,next,sort and so on*/
        /*Pagination is done by an array of objects, due angularJS's funky way of monitoring state
        with simple values */

        $scope.reloadView = function (targetSiteId) {
            var deployResource = $injector.get('DeployResource');
            deployResource.getPagedContentsWithPendingChangesByTargetSite(targetSiteId, $scope.options).then(function (data) {
                $scope.listViewResultSet = data;
                $scope.pagination = [];
                for (var i = $scope.listViewResultSet.totalPages - 1; i >= 0; i--) {
                    $scope.pagination[i] = { index: i, name: i + 1 };
                }
                if ($scope.options.pageNumber > $scope.listViewResultSet.totalPages) {
                    $scope.options.pageNumber = $scope.listViewResultSet.totalPages;
                }
            });
        };

        //assign debounce method to the search to limit the queries
        $scope.search = _.debounce(function () {
            $scope.reloadView($scope.targetSiteId);
        }, 100);

        $scope.selectAll = function ($event) {
            var checkbox = $event.target;
            if (!angular.isArray($scope.listViewResultSet.items)) {
                return;
            }
            for (var i = 0; i < $scope.listViewResultSet.items.length; i++) {
                var entity = $scope.listViewResultSet.items[i];
                entity.selected = checkbox.checked;
            }
        };

        $scope.isSelectedAll = function () {
            if (!angular.isArray($scope.listViewResultSet.items)) {
                return false;
            }
            return _.every($scope.listViewResultSet.items, function (item) {
                return item.selected;
            });
        };

        $scope.isAnythingSelected = function () {
            if (!angular.isArray($scope.listViewResultSet.items)) {
                return false;
            }
            return _.some($scope.listViewResultSet.items, function (item) {
                return item.selected;
            });
        };

        $scope.getIcon = function (entry) {
            return iconHelper.convertFromLegacyIcon(entry.icon);
        };

        $scope.deployContent = function (targetSite, contentId, contentName, contentDeploymentSettings) {

            // Asynchrynous execution 
            var deferredResult = $q.defer();

            // Error message
            //var errorMessage = "Couldn't deploy pending changes for content (with Id: " + contentId + ") on target site (with Url: " + targetSite.Url + "). ";
            var errorMessage = "Couldn't deploy pending changes for content '" + contentName + "'";

            // Load contentSync
            DeployResource.getContentSyncAndParentSync(targetSite.Id, contentId).then(
                function (response) {

                    // Check whether the parent node has been already deployed
                    if (response.data.length < 1)
                        deferredResult.reject(errorMessage + " [REASON: The parent node of the content hasn't been deployed yet]")

                    // Get contentSyncs 
                    var contentSync = response.data[0];
                    var parentContentSync = response.data[1];

                    //$log.log(contentSync);
                    //$log.log(parentContentSync);

                    // Get content 
                    var deferredGetContent = $q.defer();
                    // If the content hasnt't been deleted, then get the content data
                    if (contentSync.SyncState != 4) {
                        DeployResource.getContentById(contentId).then(
                            function (response) {
                                deferredGetContent.resolve(response.data);
                            },
                            function (error) {
                                deferredGetContent.reject(" [REASON: Couldn't find content with Id " + contentId + "]");
                            }
                        );
                    }
                    else {
                        // It is a deleted content, so no content to return!
                        deferredGetContent.resolve(null);
                    }
                    deferredGetContent.promise.then(
                        function (response) {
                            var content = response;
                            //$log.log(content);
                            // Deploy content to the target site
                            DeployResource.remoteDeployContent(targetSite.Id, content, contentSync, parentContentSync, contentDeploymentSettings).then(
                                function (response) {
                                    if (response.data.errorMessage != '') {
                                        deferredResult.reject(errorMessage + " [REASON: " + response.data.errorMessage + "]");
                                    }
                                    else {
                                        // Check whether the content has been deployed successfully
                                        if (response.data.ContentGuid != null) {
                                            // Update the contentSync entity
                                            contentSync.TargetContentGuid = response.data.ContentGuid;
                                            contentSync.SyncState = 1; //synced
                                            DeployResource.saveContentSync(contentSync).then(
                                                function (response) {
                                                    deferredResult.resolve();
                                                },
                                                function (error) {
                                                    if (error.data && error.data.Message)
                                                        errorMessage = errorMessage + " [REASON: " + error.data.Message + "]";
                                                    deferredResult.reject(errorMessage);
                                                }
                                            );
                                        }
                                        else {
                                            deferredResult.reject(errorMessage);
                                        }
                                    }
                                },
                                function (error) {
                                    if (error.data && error.data.Message)
                                        errorMessage = errorMessage + " [REASON: " + error.data.Message + "]";
                                    deferredResult.reject(errorMessage);
                                }
                            );

                        },
                        function (error) {
                            if (error.data && error.data.Message)
                                errorMessage = errorMessage + " [REASON: " + error.data.Message + "]";
                            deferredResult.reject(errorMessage + response);
                        }
                    );
                },
                function (error) {
                    deferredResult.reject(errorMessage + " [REASON: Couldn't find Sync State for content with Id " + contentId + "]")
                }
            );

            return deferredResult.promise;
        }

        $scope.deployContents = function () {

            var deferred = $q.defer();

            $scope.actionInProgress = true;
            $scope.bulkStatus = "Starting deployment";

            // First of all get target site's info
            DeployResource.getTargetSite($scope.targetSiteId).then(
                function (result) {
                    var targetSite = result.data;
                    if (!targetSite.Url) {
                        deferred.reject("The target site must have a valid Url");
                    }

                    // Get deployment settings
                    // --------------------- TODO: read the settings from the UI. now they are static and initialized only here!!!!
                    var contentDeploymentSettings = { ModifyingContentIfNotFoundCreateContent: true, DeletingContentIfNotFoundNoMessageError: true };

                    // Get all selected items in the listview
                    var selected = _.filter($scope.listViewResultSet.items, function (item) {
                        return item.selected;
                    });
                    var total = selected.length;
                    if (total === 0) {
                        deferred.reject("There are no selected items to deploy");
                    }

                    // --------------------- TODO: sort by level
                    //$log.log(selected);
                    //// Sort the selected items by the property level in order to deploy the parents first
                    //selected = _.sortBy(selected, function (item) {
                    //    return item.Level;
                    //}); 
                    //$log.log(selected);

                    // For each selected item in the listview
                    var processed = 0;
                    var successful = 0;
                    for (var i = 0; i < selected.length; i++) {
                        $scope.bulkStatus = "Deploying " + (processed + 1) + " out of " + total + " pending changes";

                        $scope.deployContent(targetSite, selected[i].id, selected[i].name, contentDeploymentSettings).then(
                            function (response) {
                                processed++;
                                successful++;
                                // Check whether it is the last iteration 
                                if (processed >= total) {
                                    deferred.resolve(successful);
                                }
                            },
                            function (error) {
                                processed++;
                                notificationsService.error(error);
                                // Check whether it is the last iteration 
                                if (processed >= total) {
                                    deferred.resolve(successful);
                                }
                            }
                        );
                    }

                },
                function (error) {
                    deferred.reject("Couldn't load target site's settings");
                }
            );

            deferred.promise.then(
                function (response) {
                    if (response > 0) {
                        notificationsService.success(response + " pending changes deployed successfully");
                    }
                    $scope.bulkStatus = "";
                    $scope.reloadView($scope.targetSiteId);
                    $scope.actionInProgress = false;
                },
                function (error) {
                    notificationsService.error(error);
                    $scope.bulkStatus = "";
                    $scope.reloadView($scope.targetSiteId);
                    $scope.actionInProgress = false;
                }
            );

        };

        if ($scope.targetSiteId > 0) {
            $scope.pagination = new Array(1);
            $scope.reloadView($scope.targetSiteId);
        }

    };



    // ---------------------------------------------- Media controller ----------------------------------------------
    function DeployEditMediaController($q, $rootScope, $scope, $routeParams, $injector, $log, notificationsService, iconHelper, dialogService, mediaResource, DeployResource) {

        $scope.entityType = "media";
        $scope.actionInProgress = false;

        $scope.listViewResultSet = {
            totalPages: 0,
            items: []
        };

        $scope.options = {
            pageSize: 50,
            pageNumber: 1,
            filter: '',
            orderBy: 'SortOrder',
            orderDirection: "asc"
        };

        $scope.targetSiteId = -1;
        if ($routeParams.id) {
            $scope.targetSiteId = $routeParams.id;
        }

        $scope.next = function () {
            if ($scope.options.pageNumber < $scope.listViewResultSet.totalPages) {
                $scope.options.pageNumber++;
                $scope.reloadView($scope.targetSiteId);
            }
        };

        $scope.goToPage = function (pageNumber) {
            $scope.options.pageNumber = pageNumber + 1;
            $scope.reloadView($scope.targetSiteId);
        };

        $scope.sort = function (field) {
            $scope.options.orderBy = field;
            if ($scope.options.orderDirection === "desc") {
                $scope.options.orderDirection = "asc";
            } else {
                $scope.options.orderDirection = "desc";
            }
            $scope.reloadView($scope.targetSiteId);
        };

        $scope.prev = function () {
            if ($scope.options.pageNumber > 1) {
                $scope.options.pageNumber--;
                $scope.reloadView($scope.targetSiteId);
            }
        };

        /*Loads the search results, based on parameters set in prev,next,sort and so on*/
        /*Pagination is done by an array of objects, due angularJS's funky way of monitoring state
        with simple values */

        $scope.reloadView = function (targetSiteId) {
            var deployResource = $injector.get('DeployResource');
            deployResource.getPagedMediasWithPendingChangesByTargetSite(targetSiteId, $scope.options).then(function (data) {
                $scope.listViewResultSet = data;
                $scope.pagination = [];
                for (var i = $scope.listViewResultSet.totalPages - 1; i >= 0; i--) {
                    $scope.pagination[i] = { index: i, name: i + 1 };
                }
                if ($scope.options.pageNumber > $scope.listViewResultSet.totalPages) {
                    $scope.options.pageNumber = $scope.listViewResultSet.totalPages;
                }
            });
        };

        //assign debounce method to the search to limit the queries
        $scope.search = _.debounce(function () {
            $scope.reloadView($scope.targetSiteId);
        }, 100);

        $scope.selectAll = function ($event) {
            var checkbox = $event.target;
            if (!angular.isArray($scope.listViewResultSet.items)) {
                return;
            }
            for (var i = 0; i < $scope.listViewResultSet.items.length; i++) {
                var entity = $scope.listViewResultSet.items[i];
                entity.selected = checkbox.checked;
            }
        };

        $scope.isSelectedAll = function () {
            if (!angular.isArray($scope.listViewResultSet.items)) {
                return false;
            }
            return _.every($scope.listViewResultSet.items, function (item) {
                return item.selected;
            });
        };

        $scope.isAnythingSelected = function () {
            if (!angular.isArray($scope.listViewResultSet.items)) {
                return false;
            }
            return _.some($scope.listViewResultSet.items, function (item) {
                return item.selected;
            });
        };

        $scope.getIcon = function (entry) {
            return iconHelper.convertFromLegacyIcon(entry.icon);
        };

        $scope.deployMedia = function (targetSite, mediaId, mediaName, mediaDeploymentSettings) {

            // Asynchrynous execution 
            var deferredResult = $q.defer();

            // Error message
            //var errorMessage = "Couldn't deploy pending changes for media (with Id: " + mediaId + ") on target site (with Url: " + targetSite.Url + "). ";
            var errorMessage = "Couldn't deploy pending changes for media '" + mediaName + "'";

            // Load mediaSync
            DeployResource.getMediaSyncAndParentSync(targetSite.Id, mediaId).then(
                function (response) {

                    // Check whether the parent node has been already deployed
                    if (response.data.length < 1)
                        deferredResult.reject(errorMessage + " [REASON: The parent node of the media hasn't been deployed yet]")

                    // Get mediaSyncs 
                    var mediaSync = response.data[0];
                    var parentMediaSync = response.data[1];

                    //$log.log(mediaSync);
                    //$log.log(parentMediaSync);

                    // Get media 
                    var deferredGetMedia = $q.defer();
                    // If the media hasnt't been deleted, then get the media data
                    if (mediaSync.SyncState != 4) {
                        DeployResource.getMediaById(mediaId).then(
                            function (response) {
                                deferredGetMedia.resolve(response.data);
                            },
                            function (error) {
                                deferredGetMedia.reject(" [REASON: Couldn't find media with Id " + mediaId + "]");
                            }
                        );
                    }
                    else {
                        // It is a deleted media, so no media to return!
                        deferredGetMedia.resolve(null);
                    }
                    deferredGetMedia.promise.then(
                        function (response) {
                            var media = response;
                            //$log.log(media);
                            // Deploy media to the target site
                            DeployResource.remoteDeployMedia(targetSite.Id, media, mediaSync, parentMediaSync, mediaDeploymentSettings).then(
                                function (response) {
                                    if (response.data.errorMessage != '') {
                                        deferredResult.reject(errorMessage + " [REASON: " + response.data.errorMessage + "]");
                                    }
                                    else {
                                        // Check whether the media has been deployed successfully
                                        if (response.data.MediaGuid != null) {
                                            // Update the mediaSync entity
                                            mediaSync.TargetMediaGuid = response.data.MediaGuid;
                                            mediaSync.SyncState = 1; //synced
                                            DeployResource.saveMediaSync(mediaSync).then(
                                                function (response) {
                                                    deferredResult.resolve();
                                                },
                                                function (error) {
                                                    if (error.data && error.data.Message)
                                                        errorMessage = errorMessage + " [REASON: " + error.data.Message + "]";
                                                    deferredResult.reject(errorMessage);
                                                }
                                            );
                                        }
                                        else {
                                            deferredResult.reject(errorMessage);
                                        }
                                    }
                                },
                                function (error) {
                                    if (error.data && error.data.Message)
                                        errorMessage = errorMessage + " [REASON: " + error.data.Message + "]";
                                    deferredResult.reject(errorMessage);
                                }
                            );

                        },
                        function (error) {
                            if (error.data && error.data.Message)
                                errorMessage = errorMessage + " [REASON: " + error.data.Message + "]";
                            deferredResult.reject(errorMessage + response);
                        }
                    );
                },
                function (error) {
                    deferredResult.reject(errorMessage + " [REASON: Couldn't find Sync State for media with Id " + mediaId + "]")
                }
            );

            return deferredResult.promise;
        }

        $scope.deployMedias = function () {

            var deferred = $q.defer();

            $scope.actionInProgress = true;
            $scope.bulkStatus = "Starting deployment";

            // First of all get target site's info
            DeployResource.getTargetSite($scope.targetSiteId).then(
                function (result) {
                    var targetSite = result.data;
                    if (!targetSite.Url) {
                        deferred.reject("The target site must have a valid Url");
                    }

                    // Get deployment settings
                    // --------------------- TODO: read the settings from the UI. now they are static and initialized only here!!!!
                    var mediaDeploymentSettings = { ModifyingMediaIfNotFoundCreateMedia: true, DeletingMediaIfNotFoundNoMessageError: true };

                    // Get all selected items in the listview
                    var selected = _.filter($scope.listViewResultSet.items, function (item) {
                        return item.selected;
                    });
                    var total = selected.length;
                    if (total === 0) {
                        deferred.reject("There are no selected items to deploy");
                    }

                    // For each selected item in the listview
                    var processed = 0;
                    var successful = 0;
                    for (var i = 0; i < selected.length; i++) {
                        $scope.bulkStatus = "Deploying " + (processed + 1) + " out of " + total + " pending changes";

                        $scope.deployMedia(targetSite, selected[i].id, selected[i].name, mediaDeploymentSettings).then(
                            function (response) {
                                processed++;
                                successful++;
                                // Check whether it is the last iteration 
                                if (processed >= total) {
                                    deferred.resolve(successful);
                                }
                            },
                            function (error) {
                                processed++;
                                notificationsService.error(error);
                                // Check whether it is the last iteration 
                                if (processed >= total) {
                                    deferred.resolve(successful);
                                }
                            }
                        );
                    }

                },
                function (error) {
                    deferred.reject("Couldn't load target site's settings");
                }
            );

            deferred.promise.then(
                function (response) {
                    if (response > 0) {
                        notificationsService.success(response + " pending changes deployed successfully");
                    }
                    $scope.bulkStatus = "";
                    $scope.reloadView($scope.targetSiteId);
                    $scope.actionInProgress = false;
                },
                function (error) {
                    notificationsService.error(error);
                    $scope.bulkStatus = "";
                    $scope.reloadView($scope.targetSiteId);
                    $scope.actionInProgress = false;
                }
            );

        };

        if ($scope.targetSiteId > 0) {
            $scope.pagination = new Array(1);
            $scope.reloadView($scope.targetSiteId);
        }

    };


    // ---------------------------------------------- register the controllers ----------------------------------------------
    angular.module("umbraco").controller('Deploy.DeployTree.DeployEditController', DeployEditController);
    angular.module("umbraco").controller('Deploy.DeployTree.DeployEditContentController', DeployEditContentController);
    angular.module("umbraco").controller('Deploy.DeployTree.DeployEditMediaController', DeployEditMediaController);

})();

