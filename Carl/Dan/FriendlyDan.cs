﻿using Newtonsoft.Json;
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
        private static FriendlyDan s_instance;

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
            s_instance = this;
        }

        public async void OnUserActivity(AdvanceUserActivity activity)
        {
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
                        await CommandUtils.GlobalWhisper(m_firehose, userId, $"@{userName} has become active in @{channelName}");
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

                List<int> channelIds = CreeperCarl.GetActiveChannelIds(friendUserid);
                if(channelIds != null && channelIds.Count > 0)
                {
                    foundSomeone = true;
                    output += $"{await MixerUtils.GetUserName(friendUserid)} is currently watching ";
                    output += await CommandUtils.FormatChannelIds(channelIds, 50);
                }
            }
            if(!foundSomeone)
            {
                output = "None of your friends are currently online.";
            }
            return output;
        }

        public static bool AreMutualFriends(int requesterId, int actionReceiverId)
        {
            return s_instance.AreMutualFriendsInternal(requesterId, actionReceiverId);
        }

        public bool AreMutualFriendsInternal(int requesterId, int actionReceiverId)
        {
            // Wa are acutally just going to check that this user is friends with us. 
            // It doesn't matter if we friended them.

            // Try to get the record for the action receiver.
            Relationships relation;
            if (m_currentSettings.Users.TryGetValue(actionReceiverId, out relation))
            {
                // Lock their following list and make sure the main user is in it.
                lock(relation.Friends)
                {
                    foreach(int friendUserId in relation.Friends)
                    {
                        if(friendUserId == requesterId)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        #region Commands

        public async void OnCommand(string command, ChatMessage msg)
        {
            if (command.Equals("friend") || command.Equals("friends"))
            {
                string secondaryCommand = CommandUtils.GetSingleWordArgument(msg.Text);
                if(secondaryCommand == null || secondaryCommand.Equals("help"))
                {
                    await CommandUtils.SendResponse(m_firehose, msg, $"Friends allows you interact with others through me and get notifications when they enter channels on Mixer. The friends subcommands are: find, list, add, remove, lurk, clear, help. Example: ^friends add Quinninator", true);
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
                await CommandUtils.SendResponse(m_firehose, msg, "You don't have any friends. 😞", true);
                return;
            }

            // Get a local copy of the friends.
            List<int> friends;
            lock (relation.Friends)
            {
                friends = relation.Friends.ToList<int>();
            }
            await CommandUtils.SendResponse(m_firehose, msg, await FindOnlineFriends(friends), true);
        }

        private async Task HandleList(ChatMessage msg)
        {
            Relationships relation;
            if (m_currentSettings.Users.TryGetValue(msg.UserId, out relation))
            {
                string output = "Your current friends are ";
                List<int> friends;
                lock (relation.Friends)
                {
                    friends = relation.Friends.ToList<int>();
                }
                output += await CommandUtils.FormatUserIds(friends, 250);
                await CommandUtils.SendResponse(m_firehose, msg, output, true);
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
                await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"Who are we {(add ? "adding" : "removing")} as a friend? Specify a user name after the command.", true);
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
            await CommandUtils.SendResponse(m_firehose, msg.ChannelId, msg.UserName, $"You're {(add ? (!addedToFriends && !addedToFollowers ? "still" : "now") : "no longer")} friends with @{await MixerUtils.GetProperUserName(friendUserName)}{(add ? "! ❤️" : ". 💔")}", true);
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
