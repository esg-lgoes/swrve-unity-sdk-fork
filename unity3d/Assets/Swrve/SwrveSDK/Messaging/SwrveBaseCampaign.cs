using UnityEngine;
using System.Collections.Generic;
using System;
using System.Linq;
using SwrveUnity.Helpers;
using SwrveUnityMiniJSON;

namespace SwrveUnity.Messaging
{
    /// <summary>
    /// Swrve messaging campaign.
    /// </summary>
    public abstract class SwrveBaseCampaign
    {
        const string ID_KEY = "id";
        const string CONVERSATION_KEY = "conversation";
        const string MESSAGE_KEY = "message";
        const string EMBEDDED_MESSAGE_KEY = "embedded_message";
        const string SUBJECT_KEY = "subject";
        const string MESSAGE_CENTER_KEY = "message_center";

        const string TRIGGERS_KEY = "triggers";
        const string EVENT_NAME_KEY = "event_name";
        const string CONDITIONS_KEY = "conditions";
        const string RULES_KEY = "rules";
        const string RANDOM_KEY = "random";

        const string DISMISS_AFTER_VIEWS_KEY = "dismiss_after_views";
        const string DELAY_FIRST_MESSAGE_KEY = "delay_first_message";
        const string MIN_DELAY_BETWEEN_MESSAGES_KEY = "min_delay_between_messages";

        const string START_DATE_KEY = "start_date";

        const string END_DATE_KEY = "end_date";

        protected readonly System.Random rnd = new System.Random();
        protected const string WaitTimeFormat = @"HH\:mm\:ss zzz";
        protected const int DefaultDelayFirstMessage = 180;
        protected const long DefaultMaxShows = 99999;
        protected const int DefaultMinDelay = 60;

        /// <summary>
        /// Name of the campaign.
        /// </summary>
        public string Name;

        /// <summary>
        /// Priority of the campaign.
        /// </summary>
        public int Priority = 9999;

        /// <summary>
        /// Identifies the campaign.
        /// </summary>
        public int Id;

        /// <summary>
        // Flag indicating if it is a MessageCenter campaign
        /// </summary>
        public bool MessageCenter
        {
            get;
            protected set;
        }

        /// <summary>
        // Message center details
        /// </summary>
        public SwrveMessageCenterDetails MessageCenterDetails;

        /// <summary>
        // MessageCenter subject of the campaign
        /// </summary>
        protected string subject;

        /// <summary>
        /// List of triggers for the campaign.
        /// </summary>
        protected List<SwrveTrigger> triggers;

        /// <summary>
        /// The start date of the campaign.
        /// </summary>
        public DateTime StartDate;

        /// <summary>
        /// The end date of the campaign.
        /// </summary>
        public DateTime EndDate;

        /// <summary>
        /// Number of impressions of this campaign. Used to disable the campaign if
        /// it reaches total impressions.
        /// </summary>
        public int Impressions
        {
            get
            {
                return this.State.Impressions;
            }
            set
            {
                this.State.Impressions = value;
            }
        }

        /// <summary>
        /// Download time of this campaign.
        /// </summary>
        public DateTime DownloadDate
        {
            get { return new DateTime(State.DownloadDate); }
            set { State.DownloadDate = value.Ticks; }
        }

        /// <summary>
        /// Get the status of the campaign.
        /// </summary>
        /// <returns>
        /// Status of the campaign.
        /// </returns>
        public SwrveCampaignState.Status Status
        {
            get
            {
                return this.State.CurStatus;
            }
            set
            {
                this.State.CurStatus = value;
            }
        }

        /**
        * @return the subject name of the campaign.
        */
        [Obsolete("Use SwrveMessageCenterDetails subject instead")]
        public string Subject
        {
            get
            {
                return subject;
            }
            protected set
            {
                this.subject = value;
            }
        }

        /// <summary>
        /// Used internally to save the state of the campaign.
        /// </summary>
        public SwrveCampaignState State;

        protected readonly DateTime swrveInitialisedTime;
        protected DateTime showMessagesAfterLaunch;
        protected DateTime showMessagesAfterDelay
        {
            get
            {
                return this.State.ShowMessagesAfterDelay;
            }
            set
            {
                this.State.ShowMessagesAfterDelay = value;
            }
        }
        protected int minDelayBetweenMessage;
        protected int delayFirstMessage = DefaultDelayFirstMessage;
        protected int maxImpressions;

        protected SwrveBaseCampaign(DateTime initialisedTime)
        {
            this.State = new SwrveCampaignState();
            this.swrveInitialisedTime = initialisedTime;
            this.triggers = new List<SwrveTrigger>();
            this.minDelayBetweenMessage = DefaultMinDelay;
            this.showMessagesAfterLaunch = swrveInitialisedTime + TimeSpan.FromSeconds(DefaultDelayFirstMessage);
        }

        public bool CheckCampaignLimits(string triggerEvent, IDictionary<string, string> payload, List<SwrveQaUserCampaignInfo> qaCampaignInfoList)
        {
            // Use local time to track throttle limits (want to show local time in logs)
            DateTime localNow = SwrveHelper.GetNow();

            if (!CanTrigger(triggerEvent, payload))
            {
                string reason = "There is no trigger in " + Id + " that matches " + triggerEvent;
                LogAndAddReason(reason, false, qaCampaignInfoList);
                return false;
            }

            if (!IsActive(qaCampaignInfoList))
            {
                return false;
            }

            if (!CheckImpressions(qaCampaignInfoList))
            {
                return false;
            }

            if (!string.Equals(triggerEvent, SwrveSDK.DefaultAutoShowMessagesTrigger, StringComparison.OrdinalIgnoreCase) && IsTooSoonToShowMessageAfterLaunch(localNow))
            {
                string reason = "{Campaign throttle limit} Too soon after launch. Wait until " + showMessagesAfterLaunch.ToString(WaitTimeFormat);
                LogAndAddReason(reason, false, qaCampaignInfoList);
                return false;
            }

            if (IsTooSoonToShowMessageAfterDelay(localNow))
            {
                string reason = "{Campaign throttle limit} Too soon after last message. Wait until " + showMessagesAfterDelay.ToString(WaitTimeFormat);
                LogAndAddReason(reason, false, qaCampaignInfoList);
                return false;
            }

            return true;
        }

        public bool CheckImpressions(List<SwrveQaUserCampaignInfo> qaCampaignInfoList = null)
        {
            if (Impressions >= maxImpressions)
            {
                string reason = "{Campaign throttle limit} Campaign " + Id + " has been shown " + maxImpressions + " times already";
                LogAndAddReason(reason, false, qaCampaignInfoList);
                return false;
            }
            return true;
        }

        public bool IsActive(List<SwrveQaUserCampaignInfo> qaCampaignInfoList = null)
        {
            // Use UTC to compare to start/end dates from DB
            DateTime utcNow = SwrveHelper.GetUtcNow();

            if (StartDate > utcNow)
            {
                string reason = string.Format("Campaign {0} not started yet (now: {1}, end: {2})", Id, utcNow, StartDate);
                LogAndAddReason(reason, false, qaCampaignInfoList);
                return false;
            }

            if (EndDate < utcNow)
            {
                string reason = string.Format("Campaign {0} has finished (now: {1}, end: {2})", Id, utcNow, EndDate);
                LogAndAddReason(reason, false, qaCampaignInfoList);
                return false;
            }

            return true;
        }

        protected void LogAndAddReason(string reason, bool displayed, List<SwrveQaUserCampaignInfo> qaCampaignInfoList)
        {
            if (SwrveQaUser.Instance.loggingEnabled && qaCampaignInfoList != null)
            {
                SwrveQaUserCampaignInfo campaignInfo = null;
                if (this is SwrveConversationCampaign)
                {
                    SwrveConversationCampaign conversationCampaign = (SwrveConversationCampaign)this;
                    campaignInfo = new SwrveQaUserCampaignInfo(Id, conversationCampaign.Conversation.Id, conversationCampaign.GetCampaignType(), displayed, reason);
                }
                else if (this is SwrveInAppCampaign)
                {
                    SwrveInAppCampaign inAppCampaign = (SwrveInAppCampaign)this;
                    campaignInfo = new SwrveQaUserCampaignInfo(Id, inAppCampaign.Message.Id, inAppCampaign.GetCampaignType(), displayed, reason);
                }
                else if (this is SwrveEmbeddedCampaign)
                {
                    SwrveEmbeddedCampaign embeddedCampaign = (SwrveEmbeddedCampaign)this;
                    campaignInfo = new SwrveQaUserCampaignInfo(Id, embeddedCampaign.Message.Id, embeddedCampaign.GetCampaignType(), displayed, reason);
                }
                if (campaignInfo != null)
                {
                    qaCampaignInfoList.Add(campaignInfo);
                }
            }
            SwrveLog.Log(string.Format("{0} {1}", this, reason));
        }

        public List<SwrveTrigger> GetTriggers()
        {
            return triggers;
        }

        /// <summary>
        /// Load an in-app campaign from a JSON response.
        /// </summary>
        /// <param name="campaignData">
        /// JSON object with the individual campaign data.
        /// </param>
        /// <param name="initialisedTime">
        /// Time that the SDK was initialised. Used for rules checking.
        /// </param>
        /// <param name="assetPath">
        /// Path to the folder that will store all the assets.
        /// </param>
        /// <returns>
        /// Parsed in-app campaign.
        /// </returns>
        public static SwrveBaseCampaign LoadFromJSON(ISwrveAssetsManager swrveAssetsManager, Dictionary<string, object> campaignData, DateTime initialisedTime, UnityEngine.Color? defaultBackgroundColor, List<SwrveQaUserCampaignInfo> qaUserCampaignInfoList)
        {
            SwrveBaseCampaign campaign = LoadFromJSONWithNoValidation(swrveAssetsManager, campaignData, initialisedTime, defaultBackgroundColor, qaUserCampaignInfoList);
            if (campaign == null)
            {
                return null;
            }

            AssignCampaignTriggers(campaign, campaignData);
            campaign.MessageCenter = campaignData.ContainsKey(MESSAGE_CENTER_KEY) && (bool)campaignData[MESSAGE_CENTER_KEY];

            if (!campaign.MessageCenter && (campaign.GetTriggers().Count == 0))
            {
                string reason = "Campaign [" + campaign.Id + "], has no triggers. Skipping this campaign.";
                campaign.LogAndAddReason(reason, false, qaUserCampaignInfoList);
                return null;
            }

            AssignCampaignRules(campaign, campaignData);
            AssignCampaignDates(campaign, campaignData);
            campaign.Subject = campaignData.ContainsKey(SUBJECT_KEY) ? (string)campaignData[SUBJECT_KEY] : "";

            return campaign;
        }

        public static SwrveBaseCampaign LoadFromJSONWithNoValidation(ISwrveAssetsManager swrveAssetsManager, Dictionary<string, object> campaignData, DateTime initialisedTime, UnityEngine.Color? defaultBackgroundColor, List<SwrveQaUserCampaignInfo> qaUserCampaignInfoList = null)
        {
            int id = MiniJsonHelper.GetInt(campaignData, ID_KEY);
            SwrveBaseCampaign campaign = null;

            if (campaignData.ContainsKey(CONVERSATION_KEY))
            {
                campaign = SwrveConversationCampaign.LoadFromJSON(swrveAssetsManager, campaignData, id, initialisedTime);
            }
            else if (campaignData.ContainsKey(MESSAGE_KEY))
            {
                campaign = SwrveInAppCampaign.LoadFromJSON(swrveAssetsManager, campaignData, id, initialisedTime, defaultBackgroundColor, qaUserCampaignInfoList);
            }
            else if (campaignData.ContainsKey(EMBEDDED_MESSAGE_KEY))
            {
                campaign = SwrveEmbeddedCampaign.LoadFromJSON(campaignData, initialisedTime, qaUserCampaignInfoList);
            }

            if (campaign == null)
            {
                return null;
            }
            campaign.Id = id;
            return campaign;
        }

        protected static void AssignCampaignTriggers(SwrveBaseCampaign campaign, Dictionary<string, object> campaignData)
        {
            IList<object> jsonTriggers = (IList<object>)campaignData[TRIGGERS_KEY];
            for (int i = 0, j = jsonTriggers.Count; i < j; i++)
            {
                object jsonTrigger = jsonTriggers[i];
                if (jsonTrigger.GetType() == typeof(string))
                {
                    jsonTrigger = new Dictionary<string, object> {
                    { EVENT_NAME_KEY, jsonTrigger },
                    { CONDITIONS_KEY, new Dictionary<string, object>() }
                };
                }

                try
                {
                    SwrveTrigger trigger = SwrveTrigger.LoadFromJson((IDictionary<string, object>)jsonTrigger);
                    campaign.GetTriggers().Add(trigger);
                }
                catch (Exception e)
                {
                    SwrveLog.LogError("Unable to parse SwrveTrigger from json " + Json.Serialize(jsonTrigger) + ", " + e);
                }
            }
        }

        protected static void AssignCampaignRules(SwrveBaseCampaign campaign, Dictionary<string, object> campaignData)
        {
            Dictionary<string, object> rules = (Dictionary<string, object>)campaignData[RULES_KEY];
            if (rules.ContainsKey(DISMISS_AFTER_VIEWS_KEY))
            {
                int totalImpressions = MiniJsonHelper.GetInt(rules, DISMISS_AFTER_VIEWS_KEY);
                campaign.maxImpressions = totalImpressions;
            }

            if (rules.ContainsKey(DELAY_FIRST_MESSAGE_KEY))
            {
                campaign.delayFirstMessage = MiniJsonHelper.GetInt(rules, DELAY_FIRST_MESSAGE_KEY);
                campaign.showMessagesAfterLaunch = campaign.swrveInitialisedTime + TimeSpan.FromSeconds(campaign.delayFirstMessage);
            }

            if (rules.ContainsKey(MIN_DELAY_BETWEEN_MESSAGES_KEY))
            {
                int minDelay = MiniJsonHelper.GetInt(rules, MIN_DELAY_BETWEEN_MESSAGES_KEY);
                campaign.minDelayBetweenMessage = minDelay;
            }
        }

        protected static void AssignCampaignDates(SwrveBaseCampaign campaign, Dictionary<string, object> campaignData)
        {
            DateTime initDate = SwrveHelper.UnixEpoch;
            campaign.StartDate = initDate.AddMilliseconds(MiniJsonHelper.GetLong(campaignData, START_DATE_KEY));
            campaign.EndDate = initDate.AddMilliseconds(MiniJsonHelper.GetLong(campaignData, END_DATE_KEY));
        }

        public void IncrementImpressions()
        {
            this.Impressions++;
        }

        protected bool IsTooSoonToShowMessageAfterLaunch(DateTime now)
        {
            return now < showMessagesAfterLaunch;
        }

        protected bool IsTooSoonToShowMessageAfterDelay(DateTime now)
        {
            return now < showMessagesAfterDelay;
        }

        protected void SetMessageMinDelayThrottle()
        {
            this.showMessagesAfterDelay = SwrveHelper.GetNow() + TimeSpan.FromSeconds(this.minDelayBetweenMessage);
        }

        /// <summary>
        /// Notify that a base message was shown to the user. This function
        /// has to be called only once when the message is displayed to
        /// the user.
        /// This is automatically called by the SDK and will only need
        /// to be manually called if you are implementing your own
        /// in-app message rendering code.
        /// </summary>
        public void WasShownToUser()
        {
            Status = SwrveCampaignState.Status.Seen;
            IncrementImpressions();
            SetMessageMinDelayThrottle();
        }

        /// <summary>
        /// Notify that the a message was dismissed.
        /// This is automatically called by the SDK and will only need
        /// to be manually called if you are implementing your own
        /// in-app message rendering code.
        /// </summary>
        public void MessageDismissed()
        {
            SetMessageMinDelayThrottle();
        }

        /// <summary>
        /// Check if this campaign will trigger for the given event and payload
        /// </summary>
        /// <returns>
        /// True if this campaign contains a message with the given trigger event.
        /// False otherwise.
        /// </returns>
        public bool CanTrigger(string eventName, IDictionary<string, string> payload = null)
        {
            return GetTriggers().Any(trig => trig.CanTrigger(eventName, payload));
        }

        #region Abstract Methods of SwrveBaseCampaign
        public abstract bool AreAssetsReady(Dictionary<string, string> personalizationProperties);

        /// <summary>
        /// Check if this campaign has valide messages for the respective orientation
        /// </summary>
        /// <returns>
        /// True if this campaign contains a message with the given orientation.
        /// False otherwise.
        /// </returns>
        public abstract bool SupportsOrientation(SwrveOrientation orientation);

        /// <summary>
        /// Return the campaign type.
        /// </summary>
        public abstract SwrveQaUserCampaignInfo.SwrveCampaignType GetCampaignType();

        #endregion
    }
}
