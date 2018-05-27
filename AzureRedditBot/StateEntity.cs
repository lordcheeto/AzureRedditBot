using Microsoft.WindowsAzure.Storage.Table;

namespace AzureRedditBot
{
    internal class StateEntity : TableEntity
    {
        public string Value { get; set; }

        public StateEntity() { }

        public StateEntity(string bot, string property, string value)
        {
            this.PartitionKey = bot;
            this.RowKey = property;
            this.Value = value;
        }
    }
}
