using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.DatabaseAnnotations;

namespace Deploy.Models.DatabasePocos
{
    [TableName("DeployContentSync")]
    [PrimaryKey("Id", autoIncrement = true)]
    [ExplicitColumns]
    public class ContentSync
    {
        public enum State : int
        {
            unknown = 0,
            synced = 1,
            created = 2,
            modified = 3,
            deleted = 4,
            pendingDelete = 5  // This state is used to mark contents as deleted, but still pending to check that the content doesn't exist anymore in the database
        }

        [Column("Id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("ContentId")]
        public int ContentId { get; set; }

        [Column("TargetSiteId")]
        public int TargetSiteId { get; set; }

        [Column("TargetContentGuid")]
        public Guid TargetContentGuid { get; set; }

        // We need to use two properties because PetaPoco returns an error when creating the table if the property type is an Enum instead of INT:
        // This property is used only to create the DB field 
        [Column("SyncState")]
        public int _SyncState { get; set; }
        // This property is a wrapper for the previous property in order to use an Enum
        [Ignore]
        public State SyncState
        {
            get
            {
                return (State)_SyncState;
            }
            set
            {
                _SyncState = (int)value;
            }
        }

        // Store content's data before the node is deleted. When the node is deleted, no information is available, so nothing to display in the
        // listview (name, last edited, updated by, ...)
        [Column("DeletedContentData")]
        [SpecialDbType(SpecialDbTypes.NTEXT)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string DeletedContentData { get; set; }

    }
}