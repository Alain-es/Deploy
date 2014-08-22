using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Deploy.Models
{
    public class ContentDeploymentSettings
    {
        public bool ModifyingContentIfNotFoundCreateContent { get; set; }
        public bool DeletingContentIfNotFoundNoMessageError { get; set; }
    }
}