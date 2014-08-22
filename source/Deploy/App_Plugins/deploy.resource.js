angular.module('umbraco.resources').factory('DeployResource', function ($q, $http, $log, umbRequestHelper, angularHelper) {
    return {

        // We need to use this method to call any GET webApi method remotely.
        // It adds to the url a queryString parameter containing the security string which allows the [AllowCrossSiteJsonAttribute] to 
        // know whether the call is originated from a website who knows the target site security key
        invokeGetMethodWithSecurityKey: function (targetSiteId, url, params) {
            var deferred = $q.defer();
            $http.get("backoffice/Deploy/DeployApi/GetTargetSite", { params: { targetSiteId: targetSiteId } }).then(
                function (response) {
                    if (response != null && response.data != null && response.data.Url != null && response.data.Url != '') {
                        var targetSiteUrl = response.data.Url + url;
                        $http.get("backoffice/Deploy/DeployApiRemoteSecurity/GetSecurityKey", { params: { targetSiteId: targetSiteId } }).then(
                            function (response) {
                                var key = response.data.replace(/(^"|"$)/g, '');    // Remove double quotes
                                if (key != null && key != undefined) {
                                    // Create the config collection with headers and parameteres
                                    var config = { headers: { AuthorizationToken: key }, params: params };
                                    // Call the GET method
                                    deferred.resolve($http.get(targetSiteUrl, config));
                                }
                            }
                        );
                    }
                }
            );
            return deferred.promise;
        },


        // We need to use this method to call any POST webApi method remotely.
        // It adds to the url a queryString parameter containing the security string which allows the [AllowCrossSiteJsonAttribute] to 
        // know whether the call is originated from a website who knows the target site security key
        invokePostMethodWithSecurityKey: function (targetSiteId, url, data) {
            var deferred = $q.defer();
            // Retrieve the security Key
            $http.get("backoffice/Deploy/DeployApi/GetTargetSite", { params: { targetSiteId: targetSiteId } }).then(
                function (response) {
                    if (response != null && response.data != null && response.data.Url != null && response.data.Url != '') {
                        var targetSiteUrl = response.data.Url + url;
                        $http.get("backoffice/Deploy/DeployApiRemoteSecurity/GetSecurityKey", { params: { targetSiteId: targetSiteId } }).then(
                            function (response) {
                                var key = response.data.replace(/(^"|"$)/g, '');    // Remove double quotes
                                if (key != null && key != undefined) {
                                    // Create the config collection with headers 
                                    var config = { headers: { AuthorizationToken: key } };
                                    // Call the POST method
                                    deferred.resolve($http.post(targetSiteUrl, data, config));
                                }
                            }
                        );
                    }
                }
            );
            return deferred.promise;
        },


        remoteTestAccess: function (targetSiteId) {
            if (!targetSiteId) {
                throw "targetSiteId cannot be null";
            }
            return this.invokeGetMethodWithSecurityKey(targetSiteId, "umbraco/backoffice/Deploy/DeployApiRemote/TestAccess");
        },

        // -----------------------------------------------------------------------------------
        // CONTENT / CONTENTSYNC
        // -----------------------------------------------------------------------------------

        getContentSyncsByTargetSite: function (targetSiteId) {
            return $http.get("backoffice/Deploy/DeployApi/GetContentSyncsByTargetSite", {
                params: { targetSiteId: targetSiteId }
            });
        },

        getPagedContentsWithPendingChangesByTargetSite: function (targetSiteId, options) {
            if (options === undefined) {
                options = {
                    pageSize: 0,
                    pageNumber: 0,
                    filter: '',
                    orderDirection: "Ascending",
                    orderBy: "SortOrder"
                };
            }
            //change asc/desct
            if (options.orderDirection === "asc") {
                options.orderDirection = "Ascending";
            }
            else if (options.orderDirection === "desc") {
                options.orderDirection = "Descending";
            }
            return umbRequestHelper.resourcePromise(
               $http.get("backoffice/Deploy/DeployApi/GetPagedContentsWithPendingChangesByTargetSite", {
                   params: {
                       targetSiteId: targetSiteId,
                       pageNumber: options.pageNumber,
                       pageSize: options.pageSize,
                       orderBy: options.orderBy,
                       orderDirection: options.orderDirection,
                       filter: options.filter
                   }
               }),
               'Failed to retrieve data for target Site ' + targetSiteId);
        },

        getContentSyncsByContentId: function (contentId) {
            return $http.get("backoffice/Deploy/DeployApi/GetContentSyncsByContentId", {
                params: { contentId: contentId }
            });
        },

        getContentSync: function (targetSiteId, contentId) {
            return $http.get("backoffice/Deploy/DeployApi/GetContentSync", {
                params: { targetSiteId: targetSiteId, contentId: contentId }
            });
        },

        getContentSyncAndParentSync: function (targetSiteId, contentId) {
            return $http.get("backoffice/Deploy/DeployApi/GetContentSyncAndParentSync", {
                params: { targetSiteId: targetSiteId, contentId: contentId }
            });
        },

        saveContentSync: function (data) {
            return $http.post("backoffice/Deploy/DeployApi/SaveContentSync", data);
        },

        getContentById: function (contentId) {
            return $http.get("backoffice/Deploy/DeployApi/GetContentById", {
                params: { contentId: contentId }
            });
        },


        // -----------------------------------------------------------------------------------
        // MEDIA / MEDIASYNC
        // -----------------------------------------------------------------------------------

        getMediaSyncsByTargetSite: function (targetSiteId) {
            return $http.get("backoffice/Deploy/DeployApi/GetMediaSyncsByTargetSite", {
                params: { targetSiteId: targetSiteId }
            });
        },

        getPagedMediasWithPendingChangesByTargetSite: function (targetSiteId, options) {
            if (options === undefined) {
                options = {
                    pageSize: 0,
                    pageNumber: 0,
                    filter: '',
                    orderDirection: "Ascending",
                    orderBy: "SortOrder"
                };
            }
            //change asc/desct
            if (options.orderDirection === "asc") {
                options.orderDirection = "Ascending";
            }
            else if (options.orderDirection === "desc") {
                options.orderDirection = "Descending";
            }
            return umbRequestHelper.resourcePromise(
               $http.get("backoffice/Deploy/DeployApi/GetPagedMediasWithPendingChangesByTargetSite", {
                   params: {
                       targetSiteId: targetSiteId,
                       pageNumber: options.pageNumber,
                       pageSize: options.pageSize,
                       orderBy: options.orderBy,
                       orderDirection: options.orderDirection,
                       filter: options.filter
                   }
               }),
               'Failed to retrieve data for target Site ' + targetSiteId);
        },

        getMediaSyncsByMediaId: function (mediaId) {
            return $http.get("backoffice/Deploy/DeployApi/GetMediaSyncsByMediaId", {
                params: { mediaId: mediaId }
            });
        },

        getMediaSync: function (targetSiteId, mediaId) {
            return $http.get("backoffice/Deploy/DeployApi/GetMediaSync", {
                params: { targetSiteId: targetSiteId, mediaId: mediaId }
            });
        },

        getMediaSyncAndParentSync: function (targetSiteId, mediaId) {
            return $http.get("backoffice/Deploy/DeployApi/GetMediaSyncAndParentSync", {
                params: { targetSiteId: targetSiteId, mediaId: mediaId }
            });
        },

        saveMediaSync: function (data) {
            return $http.post("backoffice/Deploy/DeployApi/SaveMediaSync", data);
        },

        getMediaById: function (mediaId) {
            return $http.get("backoffice/Deploy/DeployApi/GetMediaById", {
                params: { mediaId: mediaId }
            });
        },


        // -----------------------------------------------------------------------------------
        // TARGET SITE
        // -----------------------------------------------------------------------------------

        getTargetSite: function (targetSiteId) {
            return $http.get("backoffice/Deploy/DeployApi/GetTargetSite", {
                params: { targetSiteId: targetSiteId }
            });
        },

        saveTargetSite: function (data) {
            return $http.post("backoffice/Deploy/DeployApi/SaveTargetSite", data);
        },

        getTargetSiteDeleted: function (targetSiteId) {
            return umbRequestHelper.resourcePromise(
                $http.get("backoffice/Deploy/DeployApi/GetTargetSiteDeleted", {
                    params: { targetSiteId: targetSiteId }
                }),
                'Failed to delete item ' + targetSiteId
            );
        },



        // -----------------------------------------------------------------------------------
        // CURRENT SITE
        // -----------------------------------------------------------------------------------

        getCurrentSite: function (currentSiteId) {
            return $http.get("backoffice/Deploy/DeployApi/GetCurrentSite", {
                params: { currentSiteId: currentSiteId }
            });
        },

        saveCurrentSite: function (data) {
            return $http.post("backoffice/Deploy/DeployApi/SaveCurrentSite", data);
        },

        getCurrentSiteDeleted: function (currentSiteId) {
            return umbRequestHelper.resourcePromise(
                $http.get("backoffice/Deploy/DeployApi/GetCurrentSiteDeleted", {
                    params: { currentSiteId: currentSiteId }
                }),
                'Failed to delete item ' + currentSiteId
            );
        },



        // -----------------------------------------------------------------------------------
        // REMOTE CONTENT / CONTENTSYNC
        // -----------------------------------------------------------------------------------

        remoteDeployContent: function (targetSiteId, content, contentSync, parentContentSync, contentDeploymentSettings) {

            // Check that all parameters are not null
            // No check is performed for the parameter 'parentContentSync' because the parent node could be the root node or the recycle bin, so this parameter will be null/undefined
            // And also no check is performed for the parameter 'content' because it could be null when it is a deleted node
            if (!targetSiteId) {
                throw "targetSiteId cannot be null";
            }
            //if (!content) {
            //    throw "content cannot be null";
            //}
            if (!contentSync) {
                throw "contentSync cannot be null";
            }
            if (!contentDeploymentSettings) {
                throw "contentDeploymentSettings cannot be null";
            }

            var data = JSON.stringify({
                content: content,
                contentSync: contentSync,
                parentContentSync: parentContentSync,
                contentDeploymentSettings: contentDeploymentSettings
            });

            //$log.log(data);
            return this.invokePostMethodWithSecurityKey(targetSiteId, "umbraco/backoffice/Deploy/DeployApiRemote/DeployContent", JSON.stringify(data));
        },



        // -----------------------------------------------------------------------------------
        // REMOTE MEDIA / MEDIASYNC
        // -----------------------------------------------------------------------------------

        remoteDeployMedia: function (targetSiteId, media, mediaSync, parentMediaSync, mediaDeploymentSettings) {

            // Check that all parameters are not null
            // No check is performed for the parameter 'parentMediaSync' because the parent node could be the root node or the recycle bin, so this parameter will be null/undefined
            // And also no check is performed for the parameter 'media' because it could be null when it is a deleted node
            if (!targetSiteId) {
                throw "targetSiteId cannot be null";
            }
            //if (!media) {
            //    throw "media cannot be null";
            //}
            if (!mediaSync) {
                throw "mediaSync cannot be null";
            }
            if (!mediaDeploymentSettings) {
                throw "mediaDeploymentSettings cannot be null";
            }

            var data = JSON.stringify({
                media: media,
                mediaSync: mediaSync,
                parentMediaSync: parentMediaSync,
                mediaDeploymentSettings: mediaDeploymentSettings
            });

            //$log.log(data);
            return this.invokePostMethodWithSecurityKey(targetSiteId, "umbraco/backoffice/Deploy/DeployApiRemote/DeployMedia", JSON.stringify(data));
        },


    };
})

