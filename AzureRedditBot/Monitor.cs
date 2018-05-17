using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using RedditSharp;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System.Linq;
using System.Text.RegularExpressions;

namespace AzureRedditBot
{
    public static class Monitor
    {
        [FunctionName("Monitor")]
        public static void Run([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            // Pull settings from config.
            var ConnectionString = Environment.GetEnvironmentVariable("ConnectionString");
            var RedditUsername = Environment.GetEnvironmentVariable("RedditUsername");
            var RedditPassword = Environment.GetEnvironmentVariable("RedditPassword");
            var RedditClientID = Environment.GetEnvironmentVariable("RedditClientID");
            var RedditClientSecret = Environment.GetEnvironmentVariable("RedditClientSecret");
            var UserAgent = Environment.GetEnvironmentVariable("UserAgent");
            var Subreddit = Environment.GetEnvironmentVariable("Subreddit");
            var Whitelist = Environment.GetEnvironmentVariable("Whitelist");
            var Blacklist = Environment.GetEnvironmentVariable("Blacklist");
            var UserBlacklist = Environment.GetEnvironmentVariable("UserBlacklist").Split(',');
            var ThreadTimeout = Int32.Parse(Environment.GetEnvironmentVariable("ThreadTimeout"));

            // Uses Azure Storage tables to store comments, remarks, and seen links.
            CloudStorageAccount account = CloudStorageAccount.Parse(ConnectionString);
            var tableClient = account.CreateCloudTableClient();
            var commentsTable = tableClient.GetTableReference("comments");
            var remarksTable = tableClient.GetTableReference("remarks");
            var linksTable = tableClient.GetTableReference("links");

            // Connect to Reddit.
            var webAgent = new BotWebAgent(RedditUsername, RedditPassword, RedditClientID, RedditClientSecret, "http://localhost");
            WebAgent.UserAgent = UserAgent;
            var reddit = new Reddit(webAgent, true);
            var subreddit = reddit.GetSubreddit(Subreddit);
            var stream = subreddit.CommentStream.Take(25);

            // Regex expressions.
            Regex whitelist = new Regex(Whitelist, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            Regex blacklist = new Regex(Blacklist, RegexOptions.Compiled | RegexOptions.IgnoreCase);

            Random rand = new Random();

            // Partitions tables based on the bot's username.
            string partition = $"PartitionKey eq '{RedditUsername}'";

            // Comment stream will get all new comments as they are posted.
            foreach (var comment in stream)
            {
                log.Info(($"Incoming comment: {comment.AuthorName} - {comment.Body}"));

                // Continue if comment doesn't match configs.
                if (!whitelist.IsMatch(comment.Body) || blacklist.IsMatch(comment.Body) || comment.AuthorName == RedditUsername || UserBlacklist.Contains(comment.AuthorName))
                    continue;

                // Retrieve comments seen from this link.
                var comments = commentsTable.ExecuteQuerySegmentedAsync(new TableQuery<CommentEntity>().Where($"PartitionKey eq '{comment.LinkId}'"), null);

                // Determine if we've seen this thread, seen this comment, and whether we have timed out.
                bool seenThread = comments.Result.Count() > 0;
                bool seenComment = comments.Result.Count(x => x.RowKey == comment.FullName) > 0;
                bool timedout = seenThread && (DateTime.Now - comments.Result.Last().Timestamp).TotalMinutes > ThreadTimeout;

                if ((timedout || !seenThread) && !seenComment)
                {
                    var remarks = remarksTable.ExecuteQuerySegmentedAsync(new TableQuery<RemarkEntity>().Where(partition), null);
                    var links = linksTable.ExecuteQuerySegmentedAsync(new TableQuery<LinkEntity>().Where(partition), null);

                    // Grab a remark and link at random.
                    string remark = remarks.Result.ElementAt(rand.Next(remarks.Result.Count())).Remark;
                    string link = links.Result.ElementAt(rand.Next(links.Result.Count())).Uri;

                    log.Info($"Commenting! [{remark}]({link})");

                    comment.Reply($"[{remark}]({link})");

                    // Add comment to table.
                    var op = TableOperation.Insert(new CommentEntity(comment.LinkId, comment.FullName));
                    commentsTable.ExecuteAsync(op);
                }
            }
        }
    }
}
