using Microsoft.WindowsAzure.Storage.Table;

namespace AzureRedditBot
{
    internal class RemarkEntity : TableEntity
    {
        public string Remark { get; set; }

        public RemarkEntity() { }

        public RemarkEntity(string bot, string guid, string remark)
        {
            this.PartitionKey = bot;
            this.RowKey = guid;
            this.Remark = remark;
        }
    }
}