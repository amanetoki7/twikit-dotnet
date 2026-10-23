namespace Twikit;

public partial class Client
{
    /// <summary>
    /// オリジナルコンテンツ報酬プログラム（投稿の収益化）の参加条件と、このアカウントの達成状況を取得します。
    /// クリエイタースタジオの <c>/i/jf/creators/original_content_rewards</c> と同じ内容です。
    /// </summary>
    /// <remarks>
    /// 2026-10 時点の条件は「Premium / Premium+ / Premium Business に登録」「18 歳以上」
    /// 「認証済みフォロワーが 500 人以上」「過去 90 日間のホームタイムラインの認証済みインプレッションが 50 万回以上（返信は含まれません）」。
    /// 数値の条件には現在値（<see cref="CreatorRequirement.Value"/>）と進捗バー（<see cref="CreatorRequirement.Progress"/>）が付きます。
    /// </remarks>
    public async Task<CreatorEligibility> GetOriginalContentRewardsEligibilityAsync()
    {
        var response = await Jetfuel.CreatorsOriginalContentRewardsAsync().ConfigureAwait(false);
        return CreatorEligibility.Parse(CreatorProgram.OriginalContentRewards, response);
    }

    /// <summary>
    /// クリエイターサブスクリプションの参加条件と、このアカウントの達成状況を取得します。
    /// クリエイタースタジオの <c>/i/jf/creators/subscriptionsv2/eligibility</c> と同じ内容です。
    /// </summary>
    /// <remarks>
    /// 2026-10 時点の条件は「Premium に登録して認証を取得」「認証済みフォロワーが 2,000 人以上」
    /// 「過去 3 ヶ月でオーガニックインプレッションが 500 万回以上」「直近 30 日間、X で活動」「18 歳以上」「X Money の対象」。
    /// 条件を満たした後のページは未確認なので、その場合は <see cref="CreatorEligibility.Requirements"/> が空になる可能性があります。
    /// </remarks>
    public async Task<CreatorEligibility> GetCreatorSubscriptionsEligibilityAsync()
    {
        var response = await Jetfuel.CreatorsSubscriptionsEligibilityAsync().ConfigureAwait(false);
        return CreatorEligibility.Parse(CreatorProgram.Subscriptions, response);
    }
}
