using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.DatabaseAnnotations;

namespace Deploy.Models.DatabasePocos
{
    [TableName("DeployCurrentSite")]
    [PrimaryKey("Id", autoIncrement = true)]
    [ExplicitColumns]
    public class CurrentSite
    {
        [Column("Id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("Enabled")]
        public bool Enabled { get; set; }

        [Column("SecurityKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [Length(512)]
        public string SecurityKey { get; set; }
    }

}