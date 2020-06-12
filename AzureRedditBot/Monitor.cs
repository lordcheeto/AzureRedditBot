using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;
using RedditSharp;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace AzureRedditBot
{
    public static class Monitor
    {
        [FunctionName("Monitor")]
        public static void Run(
            [TimerTrigger("0 */1 * * * *")]TimerInfo timer,
            [Table("comments")] CloudTable commentsTable,
            [Table("remarks")] CloudTable remarksTable,
            [Table("links")] CloudTable linksTable,
            ILogger logger)
        {
            logger.LogInformation("Executing with version={version}", typeof(Monitor).Assembly.GetName().Version);
            
            // Pull settings from config.
            var RedditUsername = Environment.GetEnvironmentVariable("RedditUsername");
            var RedditPassword = Environment.GetEnvironmentVariable("RedditPassword");
            var RedditClientID = Environment.GetEnvironmentVariable("RedditClientID");
            var RedditClientSecret = Environment.GetEnvironmentVariable("RedditClientSecret");
            var UserAgent = Environment.GetEnvironmentVariable("UserAgent");
            var Subreddit = Environment.GetEnvironmentVariable("Subreddit");
            var SearchPattern = Environment.GetEnvironmentVariable("SearchPattern");
            var IgnorePattern = Environment.GetEnvironmentVariable("IgnorePattern");
            var UserIgnore = Environment.GetEnvironmentVariable("UserIgnore").Split(',');
            var ThreadTimeout = Int32.Parse(Environment.GetEnvironmentVariable("ThreadTimeout"));
            var TimerSlack = Int32.Parse(Environment.GetEnvironmentVariable("TimerSlack"));
            
            // Connect to Reddit.
            var webAgent = new BotWebAgent(RedditUsername, RedditPassword, RedditClientID, RedditClientSecret, "http://localhost");
            WebAgent.UserAgent = UserAgent;
            var reddit = new Reddit(webAgent, false);
            var subreddit = reddit.GetSubreddit(Subreddit);

            // Retrieve comments that have been posted since the last execution, plus some slack.
            var stream = subreddit.Comments.TakeWhile(x => x.CreatedUTC > timer.ScheduleStatus.Last - TimeSpan.FromSeconds(TimerSlack));
            
            // Regex expressions.
            Regex search = new Regex(SearchPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            Regex ignore = new Regex(IgnorePattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

            Random rand = new Random();

            // Filter string for table query; partitions tables based on the bot's username.
            string partition = $"PartitionKey eq '{RedditUsername}'";

            foreach (var comment in stream)
            {
                logger.LogDebug("Incoming comment with id={id}: {authorName} - {body}", comment.Id, comment.AuthorName, comment.Body);

                // Continue if comment doesn't match configs.
                if (!search.IsMatch(comment.Body) || ignore.IsMatch(comment.Body) || comment.AuthorName == RedditUsername || UserIgnore.Contains(comment.AuthorName))
                    continue;

                // Retrieve comments seen from this link.
                var comments = commentsTable.ExecuteQuerySegmentedAsync(new TableQuery<CommentEntity>().Where($"PartitionKey eq '{comment.LinkId}'"), null);

                // Determine if we've seen this thread, seen this comment, and whether we have timed out.
                bool seenThread = comments.Result.Count() > 0;
                bool seenComment = comments.Result.Count(x => x.RowKey == comment.FullName) > 0;
                bool timedout = seenThread && (DateTimeOffset.UtcNow - comments.Result.Last().Timestamp).TotalSeconds > ThreadTimeout;

                if ((timedout || !seenThread) && !seenComment)
                {
                    var remarks = remarksTable.ExecuteQuerySegmentedAsync(new TableQuery<RemarkEntity>().Where(partition), null);
                    var links = linksTable.ExecuteQuerySegmentedAsync(new TableQuery<LinkEntity>().Where(partition), null);

                    // Grab a remark and link at random.
                    string remark = remarks.Result.ElementAt(rand.Next(remarks.Result.Count())).Remark;
                    string link = links.Result.ElementAt(rand.Next(links.Result.Count())).Uri;

                    logger.LogInformation("Replying to comment with id={id}. [{remark}]({link})", comment.Id, remark, link);

                    if (reddit.User == null)
                        reddit.InitOrUpdateUser();

                    comment.Reply($"[{remark}]({link})");

                    // Add comment to table.
                    var op = TableOperation.Insert(new CommentEntity(comment.LinkId, comment.FullName));
                    commentsTable.ExecuteAsync(op);
                }
            }
        }
    }
}
