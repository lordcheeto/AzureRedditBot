using Microsoft.WindowsAzure.Storage.Table;

namespace AzureRedditBot
{
    internal class LinkEntity : TableEntity
    {
        public string Uri { get; set; }

        public LinkEntity() { }

        public LinkEntity(string bot, string guid, string uri)
        {
            this.PartitionKey = bot;
            this.RowKey = guid;
            this.Uri = uri;
        }
    }
}