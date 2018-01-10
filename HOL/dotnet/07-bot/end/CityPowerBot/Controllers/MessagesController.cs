using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using Microsoft.Bot.Connector;
using Newtonsoft.Json;
using Microsoft.Bot.Builder.Dialogs;
using System.IO;
using System.Net.Http.Headers;
using System.Configuration;
using System.Web;
using Newtonsoft.Json.Linq;

namespace CityPowerBot
{
    [BotAuthentication]
    public class MessagesController : ApiController
    {
        private const int IMAGE_SIZE_LIMIT = 4000000;
        public static Stream LastImage { get; set; } = null;
        public static String LastImageType { get; set; } = String.Empty;
        public static String LastImageName { get; set; } = String.Empty;
        public static String LastImageTags { get; set; } = String.Empty;
        public async Task<HttpResponseMessage> Post([FromBody]Activity activity)
        {
            if (activity.Type == ActivityTypes.Message)
            {
                // Stores send images out of order.
                var connector = new ConnectorClient(new Uri(activity.ServiceUrl));
                var imageAttachment = activity.Attachments?.FirstOrDefault(a => a.ContentType.Contains("image"));
                if (imageAttachment != null)
                {
                    LastImage = await GetImageStream(connector, imageAttachment);

                    LastImageTags = await GetImageTags(LastImage);

                    LastImageName = imageAttachment.Name;
                    LastImageType = imageAttachment.ContentType;
                    Activity reply = activity.CreateReply($"Got your image! with the following tags {LastImageTags}");
                    await connector.Conversations.ReplyToActivityAsync(reply);
                }
                else
                {
                    // Creates a dialog stack for the new conversation, adds MainDialog to the stack, and forwards all messages to the dialog stack.
                    await Conversation.SendAsync(activity, () => new MainDialog());
                }
            }
            else
            {
                HandleSystemMessage(activity);
            }
            var response = Request.CreateResponse(HttpStatusCode.OK);
            return response;
        }

        private Activity HandleSystemMessage(Activity message)
        {
            if (message.Type == ActivityTypes.DeleteUserData)
            {
                // Implement user deletion here
                // If we handle user deletion, return a real message
            }
            else if (message.Type == ActivityTypes.ConversationUpdate)
            {
                // Handle conversation state changes, like members being added and removed
                // Use Activity.MembersAdded and Activity.MembersRemoved and Activity.Action for info
                // Not available in all channels
            }
            else if (message.Type == ActivityTypes.ContactRelationUpdate)
            {
                // Handle add/remove from contact lists
                // Activity.From + Activity.Action represent what happened
            }
            else if (message.Type == ActivityTypes.Typing)
            {
                // Handle knowing tha the user is typing
            }
            else if (message.Type == ActivityTypes.Ping)
            {
            }

            return null;
        }

        private static async Task<Stream> GetImageStream(ConnectorClient connector, Attachment imageAttachment)
        {
            using (var httpClient = new HttpClient())
            {
                // The Skype attachment URLs are secured by JwtToken,
                // you should set the JwtToken of your bot as the authorization header for the GET request your bot initiates to fetch the image.
                // https://github.com/Microsoft/BotBuilder/issues/662
                var uri = new Uri(imageAttachment.ContentUrl);
                if (uri.Host.EndsWith("skype.com") && uri.Scheme == "https")
                {
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(connector));
                    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                }

                return await httpClient.GetStreamAsync(uri);
            }
        }

        /// <summary>
        /// Gets the JwT token of the bot. 
        /// </summary>
        /// <param name="connector"></param>
        /// <returns>JwT token of the bot</returns>
        private static async Task<string> GetTokenAsync(ConnectorClient connector)
        {
            var credentials = connector.Credentials as MicrosoftAppCredentials;
            if (credentials != null)
            {
                return await credentials.GetTokenAsync();
            }

            return null;
        }

        
        private static async Task<String> GetImageTags(Stream imageStream)
        {
            // Call cognitive services
            var jsonResult = string.Empty;

           using( HttpClient client = new HttpClient())
           {
                // Request headers.
                client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", ConfigurationManager.AppSettings["AZURE_COGNITIVE_SERVICES_KEY"]);
                var queryString = HttpUtility.ParseQueryString(string.Empty);
                // Request parameters. A third optional parameter is "details".
                queryString["visualFeatures"] = "Description";
                //queryString["details"] = "{string}";
                queryString["language"] = "en";
                //string requestParameters = "?visualFeatures=Categories&language=en";

                // Assemble the URI for the REST API Call.
                string uri = ConfigurationManager.AppSettings["AZURE_COGNITIVE_SERVICES_URI"] + queryString;
                
                // Request body. Posts a locally stored JPEG image.
                byte[] byteData = null;
                using (MemoryStream ms = new MemoryStream())
                {
                    imageStream.CopyTo(ms);
                    //TODO: Check if ms is not null or empty

                    // check also length
                    if (ms.Length >= IMAGE_SIZE_LIMIT)
                        throw new ArgumentException($"Images size should be less than {IMAGE_SIZE_LIMIT / 1024} Kb");

                    byteData = ms.ToArray();
                }

                using (ByteArrayContent content = new ByteArrayContent(byteData))
                //var imageUrl = "{\"url\":\"https://pbs.twimg.com/media/C8oYUqNXsAA-5xC.jpg\"}";
                //using (ByteArrayContent content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(imageUrl)))
                {
                    // This example uses content type "application/octet-stream".
                    // The other content types you can use are "application/json" and "multipart/form-data".
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    //content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    
                    // Execute the REST API call
                    var response = await client.PostAsync(uri, content);

                    // TODO: Check Response code
                    if(response.StatusCode != HttpStatusCode.OK)
                    {
                        // log
                        System.Diagnostics.Trace.TraceError($"Exception occured when calling computer vision API for picture\n Details \n\tSatus code: {response.StatusCode}, \n\t{response.ReasonPhrase}, \n\tRequest: {response.RequestMessage}");
                        return null;
                    }

                    // Get the JSON response.
                    jsonResult = await response.Content.ReadAsStringAsync();
                }
            }

            // Retrieve only tags
            JObject json = JObject.Parse(jsonResult);
            var tags = json["description"]["tags"];

            //string[] tagsText = tags.Select(t => (string)t).ToArray();
            //var result = string.Join(", ", tagsText);

            return tags.ToString();
        }
    }
}