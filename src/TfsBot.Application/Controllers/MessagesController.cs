﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.ApplicationInsights;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;
using TfsBot.Common.Db;
using TfsBot.Common.Entities;
using TfsBot.Common.Bot;

namespace TfsBot.Controllers
{
    [BotAuthentication]
    [Route("api/messages")]
    public class MessagesController : ApiController
    {
        public MessagesController(IRepository repository, Configuration configuration)
        {
            _repository = repository;
            _configuration = configuration;
        }

        private readonly IRepository _repository;
        private readonly Configuration _configuration;
        private const string SetServerCmd = "setserver:";
        private const string SetupCmd = "setup";
        private const string GetServerCmd = "getserver";
        private const string HelpCmd = "help";
        private const string GetUsersCmd = "getusers";
        private const string WelcomeMessage = "Hi I am TFS bot, you can find out more by writing **help**";

        /// <summary>
        /// POST: api/Messages
        /// Receive a message from a user and reply to it
        /// </summary>
        [HttpPost]
        public async Task<HttpResponseMessage> Post([FromBody]Activity activity)
        {
            var response = Request.CreateResponse(HttpStatusCode.OK);
            if (activity.Type == ActivityTypes.Message)
            {
                var mentions = activity.GetMentions().FirstOrDefault(i => i.Mentioned.Id == activity.Recipient.Id);
                var messageText = activity.RemoveFuzzyRecipientMention().Trim();
                TrackMessage(messageText);
                var messageTextLower = messageText.ToLowerInvariant();
                if (messageTextLower == "chat.info")
                {
                    await SendReplyAsync(activity,
                        $"conversationId: {activity.Conversation.Id}, url: {activity.ServiceUrl}");

                }

                if (messageTextLower == SetupCmd)
                {
                    var serverParams = ServerParams.New(_configuration.ServerIdPrefix);
                    await SetServerIdAsync(activity, serverParams);
                    await SendReplyAsync(activity, GetServerIdInfo(serverParams.Id, _configuration));
                    return response;
                }
                if (messageTextLower.StartsWith(SetServerCmd))
                {
                    var serverParams = ServerParams.Parse(messageText.Substring(SetServerCmd.Length));
                    if (serverParams.Id.Length == 20)
                    {
                        await SendReplyAsync(activity, $"This is not valid server id.");
                    }
                    else
                    {
                        await SetServerIdAsync(activity, serverParams);
                        await SendReplyAsync(activity, GetServerIdInfo(serverParams.Id, _configuration));
                    }
                    return response;
                }

                if (messageTextLower == GetServerCmd)
                {
                    var serverId = await GetServerIdAsync(activity);
                    await SendReplyAsync(activity, GetServerIdInfo(serverId, _configuration));
                    return response;
                }

                if (messageTextLower == HelpCmd || messageTextLower == "Settings")
                {
                    await SendReplyAsync(activity, $"You can setup your server id by writing **setup** or getting the server id by writing **getserver**");
                    return response;
                }

                if (messageTextLower == GetUsersCmd)
                {
                    var conversation = activity.Conversation;
                    await SendReplyAsync(activity, $"Conversation id: {conversation?.Id}, name: {conversation?.Name}");
                    return response;
                }

                if (messageTextLower.Contains("version"))
                {
                    messageText = "1.2";
                }
                await SendReplyAsync(activity, messageText);
            }
            else
            {
                await HandleSystemMessageAsync(activity);
            }
            
            return response;
        }

        private static string GetServerIdInfo(string serverId, Configuration configuration)
        {
            if (serverId == null)
            {
                return "You need to run **setup** first.";
            }

            var prUrl = $"{configuration.Url}/api/webhooks/pullrequest/{serverId}";
            var buildUrl = $"{configuration.Url}/api/webhooks/build/{serverId}";

            return $"Your server id was set to **{serverId}**  \n" +
                   $"_Setup your TFS webhooks to following urls:_  \n" +
                   $"pull requests:\t[{prUrl}]({prUrl})  \n" +
                   $"build:\t\t[{buildUrl}]({buildUrl})  \n";
        }

        private static void TrackMessage(string message)
        {
            var telemetry = new TelemetryClient();
            var trackParams = new Dictionary<string, string>
            {
                {"message", message},
                //{"content", contentString}
            };

            telemetry.TrackEvent("Messages.Post", trackParams);
        }

        private async Task<string> GetServerIdAsync(Activity activity)
        {
            var client = await _repository.GetClientAsync(activity.Conversation.Id, activity.Conversation.Name);
            return client?.ServerId;
        }

        private async Task SetServerIdAsync(Activity activity, ServerParams serverParams)
        {
            var serverClient = new ServerClient(serverParams.Id, activity.Conversation.Id)
            {
                UserName = activity.Conversation.Name,
                BotServiceUrl = activity.ServiceUrl,
                BotId = activity.Recipient.Id,
                BotName = activity.Recipient.Name,
                ReplaceFrom = serverParams.ReplaceFrom,
                ReplaceTo = serverParams.ReplaceTo,
            };
            await _repository.SaveServiceClient(serverClient);
            var client = new Client(serverParams.Id, activity.Conversation.Id, activity.Conversation.Name);
            await _repository.SaveClient(client);
        }

        private static async Task SendReplyAsync(Activity activity, string message)
        {
            var connector = GetConnectorClient(activity);            
            var reply = activity.CreateReply(message);
            await connector.Conversations.ReplyToActivityAsync(reply);
        }

        private static ConnectorClient GetConnectorClient(Activity activity)
        {
            var connector = new ConnectorClient(new Uri(activity.ServiceUrl));
            return connector;
        }

        private async Task<Activity> HandleSystemMessageAsync(Activity activity)
        {
            if (activity.Type == ActivityTypes.DeleteUserData)
            {
            }
            else if (activity.Type == ActivityTypes.ConversationUpdate)
            {
                if (activity.MembersAdded?.Any() == true 
                    && (activity.MembersAdded.Count != 1 
                        || activity.MembersAdded.All(i => i.Name.ToLowerInvariant() != "bot")))
                {
                    await SendReplyAsync(activity, WelcomeMessage);
                }
            }
            else if (activity.Type == ActivityTypes.ContactRelationUpdate)
            {
                await SendReplyAsync(activity, WelcomeMessage);
            }
            else if (activity.Type == ActivityTypes.Typing)
            {
                // Handle knowing tha the user is typing
            }
            else if (activity.Type == ActivityTypes.Ping)
            {
            }

            return null;
        }
    }
}