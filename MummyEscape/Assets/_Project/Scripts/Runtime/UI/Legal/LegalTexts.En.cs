namespace MummyEscape.UI.Legal
{
    /// <summary>English versions of the legal texts. Keep them in sync with the French ones (LegalTexts.cs), which prevail.</summary>
    public static partial class LegalTexts
    {
        public const string UpdatedEn = "October 6, 2026";

        /// <summary>The documents in the current game language.</summary>
        public static string[] CurrentPrivacy => Loc.Current == Loc.Lang.En ? PrivacyEn : Privacy;
        public static string[] CurrentTerms => Loc.Current == Loc.Lang.En ? TermsEn : Terms;

        public static readonly string[] PrivacyEn =
        {
            $"Version {Services.PrivacyService.PolicyVersion} — updated {UpdatedEn}. English translation: the French version prevails.",
            "# In short",
            "Mummy Rush can be played entirely offline: in that case, no data leaves your phone. " +
            "The game contains no analytics and no trackers. An ad is only shown if you choose to watch one to get energy back: " +
            "only then is Google's ad service contacted (see \"Ads\"). " +
            "You are never asked for an email address, a real name or a phone number.",
            "# Who is responsible for your data?",
            $"{Publisher}, {Address}. Contact for any question or request: {Contact}.",
            "# Data kept on your phone",
            "• Your progress (records, stars, scarabs, skins), your settings and the country you may have chosen.\n" +
            "• Your privacy choices: the date of your answer, online or offline mode, whether you are a minor and whether a parent gave consent. " +
            "Your year of birth is only used for this calculation: it is neither kept nor sent.\n" +
            "This data stays on the device; you can erase it at any time (Settings › Privacy) or by uninstalling the game.",
            "# Data processed online (only if you turn on online mode)",
            "• A random player ID, created by the authentication service, and a session token kept on your phone.\n" +
            "• Your public nickname (randomly assigned, editable). Do not use your real name.\n" +
            "• Your best scores per level (moves above the ideal path and time), public in the leaderboards, with your country only if you chose one.\n" +
            "• Your friends, your friend requests and your \"online\" status, visible to your friends.\n" +
            "• Your progress (furthest level, stars), visible to your friends only if you turn on sharing (off by default).\n" +
            "• If you use the chat: your messages, their date and the replays you share there, visible to the members of the channel (global, your guild, or the friend you write to). " +
            "Each channel keeps its last 100 messages, the oldest ones being erased as new ones arrive. Your blocks and reports are kept too, for moderation. " +
            "Private messages are only possible between friends: the game sends the server your list of friends and whether you are a minor (to close the global channel to you).\n" +
            "• If you play against others (duels, 2v2, guilds): your runs (moves and times, replayed by the server to check they are possible), " +
            "your Elo rating, your results and match history, your seals and rewards, your combat energy, your duos and your guild. " +
            "Your opponents and teammates see your nickname, your look, your Elo and the replays of your matches.\n" +
            "• If you buy golden scarabs: the product, the order ID and the purchase token sent by Google Play, the date, " +
            "and your wallet (golden scarabs, passes, paid items), kept by our server. We never receive your payment details: Google handles the payment.\n" +
            "• If you create an account (optional): your login, your password (stored encrypted by our provider, never readable by us) " +
            "and a backup of your progress, so you can restore it on another device.\n" +
            "• Technical data required by the service (IP address, device model, error logs), used for its operation and security.",
            "# Why, and on what legal basis?",
            "• Providing the online features you chose (leaderboards, friends, account, backup): performance of the terms of use (GDPR, art. 6.1.b).\n" +
            "• Checking and crediting your purchases, keeping proof of them: performance of the contract and legal accounting obligations (art. 6.1.b and 6.1.c).\n" +
            "• Securing the service, fighting cheating and abuse: legitimate interest (art. 6.1.f).\n" +
            "• Sharing your progress with your friends: your choice, which you can withdraw at any time.\n" +
            "• Personalized ads: your consent, collected by Google's form and withdrawable at any time (art. 6.1.a).\n" +
            "Below the digital age of consent in your country (15 in France), online mode requires the consent of a parent or guardian (GDPR art. 8, French Data Protection Act art. 45).",
            "# Who can see this data?",
            "• Other players: your nickname, your look (outfit, title), your duel rank, your scores and your country (if chosen); your friends also see your status, and your progress if you share it.\n" +
            "• Our technical provider, Unity Technologies (Unity Gaming Services: Authentication, Leaderboards, Friends, Cloud Save, Cloud Code), acting on our behalf and under our instructions.\n" +
            "• Google, only if you watch an ad (see \"Ads\"), and Google Play for the payment and verification of your purchases.\n" +
            "Your game data is never sold or rented, and never passed on for advertising.",
            "# Ads (only if you choose to watch one)",
            "When you run out of energy, you can watch a short ad to get some back. It is provided by Google AdMob " +
            "(Google Ireland Limited), which is independently responsible for it. Before the first one, a Google form asks, where the law requires it, " +
            "for your consent to personalized ads; you can change your mind in Settings › Privacy › Ad choices.\n" +
            "Google then processes your phone's advertising ID, your IP address and technical information about the device, to show the ad, " +
            "measure it and fight fraud. If you are a minor, you are only offered non-personalized ads. " +
            "You can reset or delete the advertising ID in the Android settings. Google's policy: policies.google.com/privacy.",
            "# Transfers outside the European Union",
            "Unity Technologies and Google LLC are based in the United States. Transfers are covered by the EU–US Data Privacy Framework " +
            "(European Commission adequacy decision of July 10, 2023) and, failing that, by the Commission's standard contractual clauses.",
            "# How long?",
            "• On your phone: until you erase it or uninstall the game.\n" +
            "• Online: as long as your profile exists. Deletion requested in the game is immediate; " +
            "profiles inactive for 3 years are deleted. Technical logs follow our provider's retention policy.\n" +
            "• Proof of purchase (order ID, product, date, player ID): 10 years, as accounting law requires, even after the account is deleted, without any other game data.",
            "# Your rights",
            "You can access your data, correct it, erase it, get it in a readable format (portability), object to or restrict its processing, " +
            "and set instructions for what happens to your data after your death. Directly in the game (Settings › Privacy):\n" +
            "• \"Export my data\": everything the game and the server know about you;\n" +
            "• \"Delete my online data\": erases your profile, your account, your scores, your friends, your backup, your duel, 2v2 and guild data, " +
            "your messages and your wallet (unspent golden scarabs are then lost);\n" +
            "• turn off online mode or progress sharing; change your nickname or country.\n" +
            $"You can also write to {Contact}: we answer within one month. " +
            "If you believe your rights are not respected, you can lodge a complaint with the CNIL (www.cnil.fr, 3 place de Fontenoy, TSA 80715, 75334 Paris Cedex 07, France) " +
            "or with the data protection authority of your country.",
            "# Security",
            "Exchanges with the server are encrypted (HTTPS). Passwords are never stored in plain text. " +
            "We collect the minimum: no email, no real name, no geolocation.",
            "# Automated decisions",
            "No decision producing legal effects is applied to you in an automated way.",
            "# Changes",
            "If this policy changes significantly, the game will show you the new version and ask for your choice again.",
        };

        public static readonly string[] AccountDeletionEn =
        {
            "This page explains how to delete your Mummy Rush account and the associated data. Mummy Rush is published by " + Publisher + ".",
            "# From the game (immediate)",
            "1. Open Mummy Rush.\n2. Go to Settings › Privacy.\n3. Tap \"Delete my online data\", then confirm.",
            "# Without the game",
            $"Write to {Contact} from any address, with the subject \"Mummy Rush account deletion\", giving your login " +
            "(or your nickname and friend code). We delete the account within 30 days at most and confirm the deletion. " +
            "To prevent abusive deletions, we may ask for proof that the account is yours (for example the friend code shown in the game).",
            "# What is deleted",
            "Your profile and its ID, your account (login and password), your nickname, your leaderboard scores, " +
            "your friends and friend requests, your shared progress and your online backup, your Elo and your duel and 2v2 history, your duos, " +
            "your place in your guild, your messages and shared replays, and your wallet (golden scarabs, passes, paid items).",
            "# What is kept",
            "Proof of purchase (order ID, product, date, player ID), for 10 years, as accounting law requires. " +
            "Duels already played stay in your opponents' history, tied to an ID that no longer leads anywhere. " +
            "Nothing else is kept by the publisher. Our provider's (Unity) technical logs are erased according to its own retention period. " +
            "The progress saved on your phone remains available offline; you can erase it in Settings › Privacy or by uninstalling the game.",
        };

        public static readonly string[] HomeEn =
        {
            "An Egyptian maze puzzle: you are the mummy, and you must escape the pitch-dark tomb, one move at a time.",
            "# Information",
            "• [Privacy policy](privacy.md)\n• [Terms of use](terms.md)\n• [Delete your account and data](account-deletion.md)\n• [Version française](../index.md)",
            "# Contact",
            Contact,
        };

        public static readonly string[] TermsEn =
        {
            $"Version {Services.PrivacyService.PolicyVersion} — updated {UpdatedEn}. English translation: the French version prevails.",
            "# Purpose",
            $"These terms govern the use of the game Mummy Rush, published by {Publisher} ({Address}, {Contact}). " +
            "By playing online, you accept them.",
            "# The game",
            "The game is free, with optional purchases. It can be played offline; the online features (leaderboards, friends, duels, guilds, chat, account, backup) are optional. " +
            "They are provided without any guarantee of permanent availability and may change or stop; your local progress then remains playable.",
            "# Energy",
            "The first act is free to play. After that, each solo game costs one energy point (10 at most) and each duel or 2v2 match one combat energy point (3 at most); " +
            "a point comes back every 6 minutes. A short, optional ad gives energy back a few times a day; the premium pass removes these limits for its season.",
            "# Purchases",
            "Golden scarabs are bought with real money on Google Play, at the price shown before the purchase. They are used to get the premium pass, pass tiers, " +
            "scarabs and cosmetic items, which give no advantage in the game. They have no monetary value and can be neither exchanged nor refunded, except for your statutory rights.\n" +
            "A purchase is credited once it has been verified with Google. Golden scarabs are kept by our server and tied to your Google Play Games account: connect it before buying. " +
            "They are lost if you delete your account.\n" +
            "By confirming a purchase, you ask for the digital content to be supplied immediately and you waive your 14-day right of withdrawal (French Consumer Code, art. L221-28 13°).\n" +
            "The scarab wheel offers random draws whose odds are shown; it is not offered in countries that ban such draws. " +
            "If you are a minor, ask a parent before any purchase. If something goes wrong with a purchase, write to us.",
            "# Age",
            "Below the digital age of consent in your country (15 in France), online mode is only available with the consent of a parent or legal guardian. Global chat is closed to minors; guild chat and messages between friends can be turned off in the game.",
            "# Your account",
            "The account is optional and consists of a login and a password, which you must keep secret. " +
            "Since no email address is requested, a forgotten password cannot be recovered. " +
            "You can delete your account and all your online data at any time from the game.",
            "# Code of conduct",
            "You may not choose an insulting, discriminatory or sexual nickname, or one impersonating someone else, " +
            "cheat (modifying the game, automation, exploiting bugs) or disrupt the service.\n" +
            "In the chat, insults, harassment, hateful or sexual content, sharing contact details and links are forbidden. " +
            "Any player can report a message or block its author; a message reported by several players is hidden until it is reviewed.\n" +
            "In case of breach, the messages and scores concerned may be removed, the chat suspended and the profile deleted.",
            "# Intellectual property",
            "The game, its graphics, its music and its code are protected. You are granted a personal, non-commercial right of use.",
            "# Liability",
            "The publisher cannot be held liable for interruptions of the online service or for progress lost because of a forgotten password " +
            "or uninstalling the game without an account. Nothing in these terms limits the rights you have under consumer law.",
            "# Personal data",
            "Its processing is described in the privacy policy, available in the game (Settings › Privacy).",
            "# Governing law",
            "These terms are governed by French law. In case of dispute, contact us first at the address above; " +
            "failing an amicable agreement, the competent courts are those provided for by law. " +
            $"In accordance with the French Consumer Code, you can use a consumer mediator free of charge: {Mediator}.",
        };
    }
}
