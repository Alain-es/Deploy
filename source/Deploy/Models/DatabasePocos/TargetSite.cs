using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.DatabaseAnnotations;

namespace Deploy.Models.DatabasePocos
{
    [TableName("DeployTargetSite")]
    [PrimaryKey("Id", autoIncrement = true)]
    [ExplicitColumns]
    public class TargetSite
    {
        [Column("Id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("SiteName")]
        [Length(512)]
        public string SiteName { get; set; }

        [Column("Url")]
        [Length(2048)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string Url { get; set; }

        [Column("SecurityKey")]
        [Length(512)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string SecurityKey { get; set; }

    }

}