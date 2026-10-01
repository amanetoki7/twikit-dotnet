# レート制限

**レート制限は 15 分ごとにリセットされます。**

\* `CreateTweetAsync` には、15 分枠とは別の、より厳しい制限があります。無料アカウントでは **1 日あたり約 50 投稿** が上限です。
これに達すると、次のメッセージとともに `CouldNotTweetException` が送出されます。

```
Authorization: You've hit the daily limit. Subscribe to Premium for higher limits. (501)
```

このとき 15 分枠のカウンターはまだ約 250 リクエスト残っていると報告するため、レート制限ヘッダーを見張るだけでは事前に気付けません。
読み取り・いいね・フォローは引き続き動作し、投稿だけが翌日まで（または Premium に加入するまで）ブロックされます。

直近のレスポンスに含まれる残りリクエスト数とリセット時刻は、`Client.RateLimitRemaining` と `Client.RateLimitReset` で確認できます。

「上限」の `-` は未計測（不明）を表します。

| メソッド                                  | 上限  | エンドポイント                      |
|-------------------------------------------|-------|-------------------------------------|
| AddMembersToGroupAsync                    | -     | AddParticipantsMutation             |
| BlockUserAsync                            | 187   | blocks/create.json                  |
| GetUserVerifiedFollowersAsync             | 500   | BlueVerifiedFollowers               |
| GetBookmarksAsync                         | 500   | Bookmarks                           |
| DeleteAllBookmarksAsync                   | -     | BookmarksAllDelete                  |
| ChangeGroupNameAsync                      | 900   | {GroupID}/update_name.json          |
| GetGroupDmHistoryAsync, GetDmHistoryAsync | 900   | conversation/{ConversationID}.json  |
| BookmarkTweetAsync                        | -     | CreateBookmark                      |
| CreatePollAsync                           | -     | cards/create.json                   |
| FollowUserAsync                           | 15    | friendships/create.json             |
| CreateListAsync                           | -     | CreateList                          |
| RetweetAsync                              | -     | CreateRetweet                       |
| CreateScheduledTweetAsync                 | -     | CreateScheduledTweet                |
| CreateTweetAsync                          | 300*  | CreateTweet                         |
| DeleteBookmarkAsync                       | -     | DeleteBookmark                      |
| DeleteDmAsync                             | -     | DMMessageDeleteMutation             |
| DeleteListBannerAsync                     | -     | DeleteListBanner                    |
| DeleteRetweetAsync                        | -     | DeleteRetweet                       |
| DeleteScheduledTweetAsync                 | -     | DeleteScheduledTweet                |
| DeleteTweetAsync                          | -     | DeleteTweet                         |
| UnfollowUserAsync                         | 187   | friendships/destroy.json            |
| EditListBannerAsync                       | -     | EditListBanner                      |
| GetFavoritersAsync                        | 500   | Favoriters                          |
| FavoriteTweetAsync                        | -     | FavoriteTweet                       |
| GetScheduledTweetsAsync                   | 500   | FetchScheduledTweets                |
| GetUserFollowersAsync                     | 50    | Followers                           |
| GetUserFollowersYouKnowAsync              | 500   | FollowersYouKnow                    |
| GetUserFollowingAsync                     | 500   | Following                           |
| GetLatestTimelineAsync                    | 500   | HomeLatestTimeline                  |
| GetTimelineAsync                          | 500   | HomeTimeline                        |
| -                                         | 450   | dm/inbox_initial_state.json         |
| AddListMemberAsync                        | -     | ListAddMember                       |
| GetListAsync                              | 500   | ListByRestId                        |
| GetListTweetsAsync                        | 500   | ListLatestTweetsTimeline            |
| GetListsAsync                             | 500   | ListsManagementPageTimeline         |
| GetListMembersAsync                       | 500   | ListMembers                         |
| RemoveListMemberAsync                     | -     | ListRemoveMember                    |
| GetListSubscribersAsync                   | 500   | ListSubscribers                     |
| LogoutAsync                               | 187   | account/logout.json                 |
| AddReactionToMessageAsync                 | -     | useDMReactionMutationAddMutation    |
| RemoveReactionFromMessageAsync            | -     | useDMReactionMutationRemoveMutation |
| MuteUserAsync                             | 187   | mutes/users/create.json             |
| GetNotificationsAsync("All")              | 180   | notifications/all.json              |
| GetNotificationsAsync("Mentions")         | 180   | notifications/mentions.json         |
| GetNotificationsAsync("Verified")         | 180   | notifications/verified.json         |
| GetRetweetersAsync                        | 500   | Retweeters                          |
| SearchTweetAsync, SearchUserAsync         | 50    | SearchTimeline                      |
| SendDmAsync                               | 187   | dm/new2.json                        |
| UserIdAsync                               | -     | account/settings.json               |
| GetUserSubscriptionsAsync                 | 500   | UserCreatorSubscriptions            |
| LoginAsync                                | 187   | onboarding/task.json                |
| GetTrendsAsync                            | 20000 | guide.json                          |
| GetTweetByIdAsync                         | 150   | TweetDetail                         |
| UnblockUserAsync                          | 187   | blocks/destroy.json                 |
| UnfavoriteTweetAsync                      | -     | UnfavoriteTweet                     |
| UnmuteUserAsync                           | 187   | mutes/users/destroy.json            |
| EditListAsync                             | -     | UpdateList                          |
| UploadMediaAsync                          | -     | media/upload.json                   |
| GetUserByIdAsync                          | 500   | UserByRestId                        |
| GetUserByScreenNameAsync                  | 95    | UserByScreenName                    |
| GetUserTweetsAsync(..., "Likes")          | 500   | Likes                               |
| GetUserTweetsAsync(..., "Media")          | 500   | UserMedia                           |
| GetUserTweetsAsync(..., "Tweets")         | 50    | UserTweets                          |
| GetUserTweetsAsync(..., "Replies")        | 50    | UserTweetsAndReplies                |
| VoteAsync                                 | -     | capi/passthrough/1                  |
| CreateGroupAsync                          | -     | dm/new2.json                        |
| DeleteDmConversationAsync                 | -     | dm/conversation/{id}/delete.json    |
| DeleteListAsync                           | -     | DeleteList                          |
| GetAboutAccountAsync                      | -     | AboutAccountQuery                   |
| GetBlockedUsersAsync                      | -     | BlockedAccountsAll                  |
| GetDmInboxAsync                           | -     | dm/inbox_initial_state.json         |
| GetMutedUsersAsync                        | -     | MutedAccounts                       |
| GetUserListsAsync                         | -     | CombinedLists                       |
| GetUserSpotlightsAsync                    | -     | ProfileSpotlightsQuery              |
| UpdateProfileAsync                        | -     | account/update_profile.json         |
