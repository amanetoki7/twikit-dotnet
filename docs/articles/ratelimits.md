# レート制限

**レート制限は 15 分ごとにリセットされます。**

\* `CreateTweetAsync` には、15 分枠とは別の、より厳しい制限があります。無料アカウントでは **1 日あたり約 50 投稿** が上限です。
これに達すると、次のメッセージとともに `CouldNotTweetException` が送出されます。

```
Authorization: You've hit the daily limit. Subscribe to Premium for higher limits. (501)
```

このとき 15 分枠のカウンターはまだ約 250 リクエスト残っていると報告するため、レート制限ヘッダーを見張るだけでは事前に気付けません。
読み取り・いいね・フォローは引き続き動作し、投稿だけが翌日まで（または Premium に加入するまで）ブロックされます。

† `PinTweetAsync` と `UnpinTweetAsync` のレート制限ヘッダーは 187 リクエストの共有枠を示しますが、X が実際に適用するのはそれではありません。2 つはそれぞれ別に数えられ、枠内で **100 回** 呼ぶと `429` で止められます（`UnpinTweetAsync` が止まっても `PinTweetAsync` はまだ通り、ヘッダーは残り 87 と報告したままです）。`NotFoundException` で失敗した呼び出しも数えられ、`api.x.com` ではなく `x.com/i/api` を経由しても同じ制限に当たります。枠は固定で、最初の呼び出しから始まり、`x-rate-limit-reset` が示す枠の終わりでちょうど解除されます。間隔は関係なく、連続呼び出しでも 8 秒おきでも 101 回目で止められました。9 アカウント・24 枠で計測し、すべて 100 でした。

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
| HighlightTweetAsync                       | -     | CreateHighlight                     |
| CreatePollAsync                           | -     | cards/create.json                   |
| FollowUserAsync                           | 15    | friendships/create.json             |
| CreateListAsync                           | -     | CreateList                          |
| RetweetAsync                              | -     | CreateRetweet                       |
| CreateScheduledTweetAsync                 | -     | CreateScheduledTweet                |
| CreateDraftTweetAsync                     | 500   | CreateDraftTweet                    |
| CreateTweetAsync                          | 300*  | CreateTweet                         |
| DeleteBookmarkAsync                       | -     | DeleteBookmark                      |
| UnhighlightTweetAsync                     | -     | DeleteHighlight                     |
| DeleteDmAsync                             | -     | DMMessageDeleteMutation             |
| DeleteListBannerAsync                     | -     | DeleteListBanner                    |
| DeleteRetweetAsync                        | -     | DeleteRetweet                       |
| DeleteScheduledTweetAsync                 | -     | DeleteScheduledTweet                |
| DeleteDraftTweetsAsync                    | 50    | DeleteDraftTweets                   |
| DeleteDraftTweetAsync                     | 500   | DeleteDraftTweet                    |
| DeleteTweetAsync                          | -     | DeleteTweet                         |
| UnfollowUserAsync                         | 187   | friendships/destroy.json            |
| EditListBannerAsync                       | -     | EditListBanner                      |
| GetFavoritersAsync                        | 500   | Favoriters                          |
| FavoriteTweetAsync                        | -     | FavoriteTweet                       |
| GetScheduledTweetsAsync                   | 500   | FetchScheduledTweets                |
| GetDraftTweetsAsync                       | 500   | FetchDraftTweets                    |
| GetUserFollowersAsync                     | 50    | Followers                           |
| GetUserFollowersYouKnowAsync              | 500   | FollowersYouKnow                    |
| GetUserFollowingAsync                     | 500   | Following                           |
| IsTweetHighlightedAsync                   | -     | HighlightTweetSheetQuery            |
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
| PinTweetAsync                             | 100†  | account/pin_tweet.json              |
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
| UnpinTweetAsync                           | 100†  | account/unpin_tweet.json            |
| EditListAsync                             | -     | UpdateList                          |
| UploadMediaAsync                          | -     | media/upload.json                   |
| GetUserByIdAsync                          | 500   | UserByRestId                        |
| GetUserByScreenNameAsync                  | 95    | UserByScreenName                    |
| GetUserHighlightsTweetsAsync              | -     | UserHighlightsTweets                |
| GetOriginalContentRewardsEligibilityAsync | 5000  | jfapi/creators/original_content_rewards |
| GetCreatorSubscriptionsEligibilityAsync   | 5000  | jfapi/creators/subscriptionsv2/eligibility |
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
