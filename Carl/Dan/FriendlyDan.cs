using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Carl.Dan
{
    class Relationships
    {
        public DateTime LastFriendsCheckTime = DateTime.MinValue;
        public DateTime LastOnlineAnnounceTime = DateTime.MinValue;
        public bool IsLurking = false;
        public List<int> Friends = new List<int>();
        public List<int> UsersWhoFriended = new List<int>();
    }

    class Settings
    {
        public int Version;
        public ConcurrentDictionary<int, Relationships> Users;
    }

    public class FriendlyDan : Dan, IFirehoseUserActivityListener, IFirehoseCommandListener
    {
        TimeSpan c_minTimeBetweenFriendsCheck = new TimeSpan(0, 10, 0);
        TimeSpan c_minTimeBetweenOnlineAnnounce = new TimeSpan(0, 5, 0);

        const string c_fileName = "UserFriends.json";
        Settings m_currentSettings;

        public FriendlyDan(IFirehose firehose)
            : base(firehose)
        {
            Setup();
            m_firehose.SubUserActivity(this);
            m_firehose.SubCommandListener(this);
        }

        public async void OnUserActivity(UserActivity activity)
        {
            // Ignore if creeper dan is letting us know. (it's from the mixer api)
            if(activity.IsFromCreeperDan)
            {
                return;
            }

            // Only look at joins.
            if(!activity.IsJoin)
            {
                return;
            }

            // See if we have a notification setup for this user.
            Relationships relation;
            if (m_currentSettings.Users.TryGetValue(activity.UserId, out relation))
            {
                // Only send out notifications every so often.
                bool doFriendsCheck = (DateTime.Now - relation.LastFriendsCheckTime) > c_minTimeBetweenFriendsCheck;
                bool doOnlineAnnounce = !relation.IsLurking && (DateTime.Now - relation.LastOnlineAnnounceTime) > c_minTimeBetweenOnlineAnnounce;
                if (!doFriendsCheck && !doOnlineAnnounce)
                {
                    return;
                }
                relation.LastFriendsCheckTime = relation.LastOnlineAnnounceTime = DateTime.Now;

                // Get the user data
                string userName = await MixerUtils.GetUserName(activity.UserId);
                if (userName == null)
                {
                    return;
                }
                string channelName = await MixerUtils.GetChannelName(activity.ChannelId);
                if (channelName == null)
                {
                    return;
                }

                if (doOnlineAnnounce)
                {
                    // Send notifications that the user is now online
                    List<int> notifiy = null;
                    lock (relation.UsersWhoFriended)
                    {
                        notifiy = relation.UsersWhoFriended.ToList<int>();
                    }
                    foreach (var userId in notifiy)
                    {
                        await CommandUtils.GlobalWhisper(m_firehose, userId, $"{userName} has become active in @{channelName}");
                    }
                }

                if (doFriendsCheck)
                {
                    // Tell them what friends they have online.
                    List<int> friends;
                    lock (relation.Friends)
                    {
                        friends = relation.Friends.ToList<int>();
                    }
                    if (friends.Count > 0)
                    {
                        await CommandUtils.GlobalWhisper(m_firehose, activity.UserId, userName, await FindOnlineFriends(friends));
                    }
                }
            }
        }

        private async Task<string> FindOnlineFriends(List<int> friendUserIds)
        {
            string output = "";
            bool foundSomeone = false;
            foreach(int friendUserid in friendUserIds)
            {
                if(foundSomeone)
                {
                    output += "; ";
                }

                List<int> channelIds = CreeperDan.GetActiveChannelIds(friendUserid);
                if(channelIds != null && channelIds.Count > 0)
                {
                    foundSomeone = true;
                    output += $"{await MixerUtils.GetUserName(friendUserid)} is currently watching ";
                    bool first = true;
                    foreach(int channelId in channelIds)
                    {
                        if(!first)
                        {
                            output += ", ";
                        }
                        first = false;
                        output += $"@{await MixerUtils.GetChannelName(channelId)}";                        
                    }
                }
            }
            if(!foundSomeone)
            {
                output = "None of your friends are currently online.";
            }
            return output;
        }

        #region Commands

        public async void OnCommand(string command, ChatMessage msg)
        {
            if (command.Equals("friend") || command.Equals("friends"))
            {
                string secondaryCommand = CommandUtils.GetSingleWordArgument(msg.Text);
                if(secondaryCommand == null || secondaryCommand.Equals("help"))
                {
                    await CommandUtils.SendResponse(m_firehose, msg, $"Friends allows me to notify you when people you know join Mixer channels. Friend sub commands: add, remove, find, list, lurk, clear, help.", true);
                    return;
                }
                if (secondaryCommand.Equals("add"))
                {
                    await HandleAddOrRemove(msg, true);
                }
                else if (secondaryCommand.Equals("remove"))
                {
                    await HandleAddOrRemove(msg, false);
                }
                else if (secondaryCommand.Equals("list"))
                {
                    await HandleList(msg);
                }
                else if (secondaryCommand.Equals("clear"))
                {
                    await HandleClear(msg);
                }
                else if (secondaryCommand.Equals("find"))
                {
                    await HandleFind(msg);
                }
                else if (secondaryCommand.Equals("lurk"))
                {
                    await HandleLurk(msg);
                }
            }
            else if(command.Equals("lurk"))
            {
                await HandleLurk(msg);
            }
        }

        private async Task HandleFind(ChatMessage msg)
        {
            // See if we have a notification setup for this user.
            Relationships relation;
            if (!m_currentSettings.Users.TryGetValue(msg.UserId, out relation))
            {
                await CommandUtils.GlobalWhisper(m_firehose, msg.UserId, msg.UserName, "You don't have any friends.");
                return;
            }

            // Get a local copy of the friends.
            List<int> friends;
            lock (relation.Friends)
            {
                friends = relation.Friends.ToList<int>();
            }
            await CommandUtils.GlobalWhisper(m_firehose, msg.UserId, msg.UserName, await FindOnlineFriends(friends));
        }

        private async Task HandleList(ChatMessage msg)
        {
            Relationships relation;
            if (m_currentSettings.Users.TryGetValue(msg.UserId, out relation))
            {
                string output = "Your current friends are ";
                bool first = true;
                List<int> friends;
                lock (relation.Friends)
                {
                    friends = relation.Friends.ToList<int>();
                }
                foreach (int friendId in relation.Friends)
                {
                    if (!first) output += ", ";
                    first = false;
                    output += await MixerUtils.GetUserName(friendId);
                }
                await CommandUtils.SendResponse(m_firehose, msg, output);
            }
            else
            {
                await CommandUtils.SendResponse(m_firehose, msg, "It doesn't look like you have any friends yet. 😞", true);
                return;
            }
        }

        private async Task HandleAddOrRemove(ChatMessage msg, bool add)
        {
            // Get the target they want to add
            string friendUserName = CommandUtils.GetSecondSingleWordArgument(msg.Text);
            if (friendUserName == null)
            {
                await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"Who are we {(add ? "adding" : "removing")} as a friend? Specify a user name after the command.", msg.IsWhisper);
                return;
            }
            int? friendUserId = await MixerUtils.GetUserId(friendUserName);
            if(!friendUserId.HasValue)
            {
                await CommandUtils.SendMixerUserNotFound(m_firehose, msg, friendUserName);
                return;
            }

            // Add the friend to their list
            bool addedToFriends = UpdateList(msg.UserId, friendUserId.Value, add, true);
            bool addedToFollowers = UpdateList(friendUserId.Value, msg.UserId, add, false);
            await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"You're {(add ? (!addedToFriends && !addedToFollowers ? "still" : "now") : "no longer")} friends with @{friendUserName}{(add ? "! ❤️" : ". 💔")}", msg.IsWhisper);
            SaveSettings();
        }

        private async Task HandleClear(ChatMessage msg)
        {
            Relationships relation = null;
            m_currentSettings.Users.TryGetValue(msg.UserId, out relation);
            if (relation == null || relation.Friends.Count == 0)
            {
                await CommandUtils.SendResponse(m_firehose, msg, "I didn't find any friends to clear. 😭");
                return;
            }

            // Delete the friend pairings.
            List<int> toRemove = new List<int>();
            lock(relation.Friends)
            {
                toRemove = relation.Friends.ToList<int>();
                relation.Friends.Clear();
            }

            // Delete the followings
            foreach (int friendUserId in relation.Friends)
            {
                UpdateList(friendUserId, msg.UserId, false, false);
            }

            await CommandUtils.SendResponse(m_firehose, msg, "I cleared out all of your friends. Who needs them anyways?");
            SaveSettings();            
            return;
        }

        private async Task HandleLurk(ChatMessage msg)
        {
            bool isLurking = true;
            while (true)
            {
                Relationships relation;
                if (m_currentSettings.Users.TryGetValue(msg.UserId, out relation))
                {
                    relation.IsLurking = !relation.IsLurking;
                    isLurking = relation.IsLurking;
                    break;
                }
                else
                {
                    Relationships rel = new Relationships()
                    {
                        IsLurking = true
                    };
                    if(m_currentSettings.Users.TryAdd(msg.UserId, rel))
                    {
                        break;
                    }
                }
            }
            await CommandUtils.SendResponse(m_firehose, msg, (isLurking ? "You're now lurking. 🙈 Your friends won't be notified when you join channels. Use ^lurk to stop lurking." : "No longer lurking, welcome back! 👋"));
            SaveSettings();
        }

#endregion

        private bool UpdateList(int lookupUser, int updateUser, bool add, bool asFriend)
        {
            while (true)
            {
                Relationships relation;
                if (m_currentSettings.Users.TryGetValue(lookupUser, out relation))
                {
                    // Get the list.
                    List<int> list = asFriend ? relation.Friends : relation.UsersWhoFriended;
                    lock (list)
                    {
                        if(add)
                        {
                            if(list.Contains(updateUser))
                            {
                                return false;
                            }
                            list.Add(updateUser);
                            return true;
                        }
                        else
                        {
                            return list.Remove(updateUser);
                        }
                    }
                }
                else
                {
                    if (!add)
                    {
                        return false;
                    }

                    // Make a new relationship
                    Relationships rel = new Relationships();

                    // Add them.
                    if (asFriend)
                    {
                        rel.Friends.Add(updateUser);
                    }
                    else
                    {
                        rel.UsersWhoFriended.Add(updateUser);
                    }

                    // Try to add it to the map
                    if (m_currentSettings.Users.TryAdd(lookupUser, rel))
                    {
                        return true;
                    }                   
                }
            }
        }

        private void Setup()
        {
            // Try to load existing settings
            if (File.Exists(c_fileName))
            {
                try
                {
                    using (StreamReader file = File.OpenText(c_fileName))
                    {
                        JsonSerializer serializer = new JsonSerializer();
                        m_currentSettings = (Settings)serializer.Deserialize(file, typeof(Settings));
                        return;
                    }
                }
                catch (Exception e)
                {
                    Logger.Error("Failed to read current settings file.", e);
                }
            }

            Logger.Info("Creating a new notification settings file.");
            m_currentSettings = new Settings()
            {
                Version = 1,
                Users = new ConcurrentDictionary<int, Relationships>()
            };
            SaveSettings();
        }

        private bool SaveSettings()
        {
            try
            {
                using (StreamWriter file = File.CreateText(c_fileName))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    serializer.Serialize(file, m_currentSettings, typeof(Settings));
                    return true;
                }
            }
            catch (Exception e)
            {
                Logger.Error("Failed to write current settings file.", e);
            }
            return false;
        }
    }
}
