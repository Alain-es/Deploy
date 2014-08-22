using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Deploy.Models
{
    public class MediaDeploymentSettings
    {
        public bool ModifyingMediaIfNotFoundCreateMedia { get; set; }
        public bool DeletingMediaIfNotFoundNoMessageError { get; set; }
    }
}