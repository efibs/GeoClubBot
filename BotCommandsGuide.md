# 🌍 GeoClubBot — Command Guide

Hey everyone! This bot helps us run our GeoGuessr club: tracking activity, linking accounts, sending reminders, and more. Below is a full rundown of every command you can use, written so you can follow along whether you've never touched a slash command before or you're already a Discord pro.

---

## 📚 First Things First — How Commands Work

This bot uses two kinds of interactions: **slash commands** and **user commands**. Here's how to use each.

### 1. Slash Commands
Slash commands are the main way to interact with the bot. To use one:
1. Click in the message box at the bottom of any channel (in this server).
2. Type `/` — a popup appears with a list of available commands.
3. Start typing the command name (e.g. `daily-reminder`) and Discord will filter the list.
4. Click the command, then fill in any parameters Discord asks you for.
5. Press **Enter** to send it.

Commands in this bot are grouped under a prefix (for example, everything for reminders lives under `/daily-reminder`). When you type `/daily-reminder`, Discord will show you the available sub-commands like `set`, `stop`, or `status`.

Most of the bot's replies are **ephemeral** — meaning only *you* can see the response. So feel free to experiment without spamming the channel.

### 2. User Commands (right-click menu)
Some commands can be triggered directly on another person:
1. **Right-click** (or long-press on mobile) a user, anywhere — in chat, the member list, etc.
2. Hover over **Apps**.
3. Pick the command you want to run on that user (e.g. `gg-nickname`).

User commands are basically a shortcut that runs a slash command with that user pre-filled as the parameter.

---

# ✨ Features & Commands

Below, commands are grouped by feature so you can find what you need quickly.

---

## 🔗 Feature: Linking Your GeoGuessr Account
Many of the other features only work once your Discord account is linked to your GeoGuessr account. Linking is a one-time process. It uses a one-time password that you send to an admin **inside GeoGuessr** to prove the account is really yours.

### `/gg-account link`
Starts the linking process for your account.

**Parameters:**
- `shareProfileLink` *(required)* — the share link to your GeoGuessr profile. It should look like `https://www.geoguessr.com/user/62c353a29d0d57e7b9a3383f`.
  - To get this link: open GeoGuessr → top right → **Profile** → click the **share button** to the left of *EDIT AVATAR* → copy the link.

**What happens next:** The bot replies (only to you) with a one-time password. **Send that password as a direct message to an admin *inside GeoGuessr*** (not in Discord!). An admin will then confirm the link and you'll be notified.

---

## ⏰ Feature: Daily Mission Reminder
Reminds you (via DM) every day to do your GeoGuessr daily mission, at a time you choose. The reminder is sent as a direct message from the bot.

### `/daily-reminder set`
Sets up (or updates) your reminder.

**Parameters:**
- `time` *(required)* — the time you want to be reminded, in 24-hour `HH:mm` format. Example: `09:00`, `21:30`.
- `timezone` *(optional)* — an IANA timezone ID, e.g. `Europe/Berlin`, `America/New_York`, `Asia/Tokyo`. If you leave it blank, the bot uses **UTC**.
- `message` *(optional)* — a custom reminder message. If left blank, the bot uses a default message.

### `/daily-reminder stop`
Turns off your daily reminder. No parameters.

### `/daily-reminder status`
Shows your current reminder settings: time, timezone, custom message, and when it was last sent. No parameters.

---

## 📊 Feature: Your Personal Activity
See how you're doing in the club.

### `/my-activity current-week`
Shows your daily mission progress for the current week: total XP earned, days completed, and a visual progress bar (🟩 done, ⬛ missed) for each day.

No parameters. Requires your GeoGuessr account to be linked (see `/gg-account link`).

### `/my-activity last-days`
Shows your daily mission progress over the last several days — handy if you want to see a rolling window instead of just the current calendar week.

**Parameters:**
- `days` *(optional)* — how many days back to include, from `1` to `14`. Defaults to `7`.

Requires your GeoGuessr account to be linked (see `/gg-account link`).

---

## 🏆 Feature: Club Stats
Check how the club as a whole is performing.

### `/club-stats todays-xp`
Shows how much XP a club has earned today.

**Parameters:**
- `clubName` *(optional)* — the name of the club. If left blank, the default club is used.
- `includeWeeklies` *(optional, true/false)* — whether to include XP from weekly challenges. Defaults to `false`.

---

## 👤 Feature: User Info
Look up information about other members and connect Discord ↔ GeoGuessr identities.

### `/user-info gg-nickname`
Tells you what GeoGuessr nickname a Discord user is linked to.

**Parameters:**
- `user` *(required)* — pick a member of the server.

Also available as a **user command**: right-click a member → **Apps** → **GeoGuessr Nickname**.

### `/user-info gg-profile`
Shows a full GeoGuessr profile for a Discord user — country, member-since date, account type, level, rating, status (good standing / banned / suspended / chat banned), and their club.

**Parameters:**
- `user` *(required)* — pick a member of the server.

Also available as a **user command**: right-click a member → **Apps** → **GeoGuessr Profile**.

### `/user-info gg-ranked`
Shows a GeoGuessr ranked-stats card for a Discord user — division, current and peak rating per game mode (overall / move / no-move / NMPZ), win streak, guessed-first rate, a visualization of their recent games (🟩 won / 🟥 lost), and their best and worst countries by flag.

**Parameters:**
- `user` *(required)* — pick a member of the server.

Also available as a **user command**: right-click a member → **Apps** → **GeoGuessr Ranked Stats**.

### `/user-info discord-user`
The reverse lookup: give it a GeoGuessr nickname and it tells you which Discord user that is.

**Parameters:**
- `nickname` *(required)* — the GeoGuessr nickname (case-sensitive).

---

## 🎭 Feature: Self-Roles
Pick optional roles for yourself (e.g. notification opt-ins, regional roles) without needing an admin to assign them.

### `/self-roles select`
Opens a private menu where you can tick or untick each available role. Roles you already have appear pre-selected; choose the final set you want and confirm. The bot updates your roles and tells you what changed.

No parameters.

---

# 💡 Tips
- All bot replies are **only visible to you** unless stated otherwise — so don't worry about cluttering channels.
- If a command fails with an "internal error" message, try again later. If it keeps happening, ping an admin.
- Commands and their parameters auto-complete as you type, so you don't need to memorize anything — just type `/` and explore.