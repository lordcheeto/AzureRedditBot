using Microsoft.WindowsAzure.Storage.Table;

namespace AzureRedditBot
{
    internal class CommentEntity : TableEntity
    {
        public CommentEntity() { }

        public CommentEntity(string linkId, string id)
        {
            this.PartitionKey = linkId;
            this.RowKey = id;
        }
    }
}