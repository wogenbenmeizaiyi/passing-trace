using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Core.Storylines;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace PassingTrace.Infrastructure.Persistence.Ai;

public sealed class PersonalRecordQueries(TraceDbContext db) : IPersonalRecordQueries
{
    private IQueryable<Event> ReadableEvents(long userId) => SocialRecordAccess.ReadableEvents(db, userId);

    private async Task<IQueryable<Event>> RecordsAsync(long userId, Guid? participantFriendId, CancellationToken ct)
    {
        var visible = ReadableEvents(userId);
        if (participantFriendId is not { } friendId) return visible;
        var friend = await db.Friendships.AsNoTracking().SingleOrDefaultAsync(x => x.Id == friendId && x.Active &&
            (x.FirstUserId == userId || x.SecondUserId == userId), ct)
            ?? throw new DomainValidationException("这位好友已不可查询，请重新查找好友。");
        var other = friend.FirstUserId == userId ? friend.SecondUserId : friend.FirstUserId;
        return visible.Where(e => e.UserId == other || db.EventParticipants.Any(p => p.EventId == e.Id && p.UserId == other && p.Active &&
            db.Friendships.Any(f => f.Id == p.FriendshipId && f.Active)));
    }

    private IQueryable<StorylineSearchIndex> StorylineIndexes(long userId, StorylineSearchFilter filter)
    {
        var indexes = db.StorylineSearchIndexes.AsNoTracking().Where(x => x.UserId == userId && x.IsCurrent &&
            db.Storylines.Any(s => s.Id == x.StorylineId && s.UserId == userId && s.DeletedAt == null));
        if (filter.Category is { } category)
            indexes = indexes.Where(x => db.Storylines.Any(s => s.Id == x.StorylineId && s.UserId == userId && s.CategoryKey == category));
        if (filter.Status is { } status)
            indexes = indexes.Where(x => db.Storylines.Any(s => s.Id == x.StorylineId && s.UserId == userId && s.Status == status));
        return indexes;
    }

    public async Task<IReadOnlyList<Guid>> RankStorylinesAsync(long userId, StorylineSearchFilter filter,
        AiSearchOrder order, float[]? vector, CancellationToken cancellationToken)
    {
        var indexes = StorylineIndexes(userId, filter);
        var ranked = order switch
        {
            AiSearchOrder.Recent => indexes.OrderByDescending(x => x.UpdatedAt),
            AiSearchOrder.Text => indexes.OrderByDescending(x => EF.Functions.TrigramsSimilarity(x.RetrievalText, filter.Query)),
            AiSearchOrder.Vector => indexes.Where(x => EF.Property<Vector?>(x, "Embedding") != null)
                .OrderBy(x => EF.Property<Vector>(x, "Embedding").CosineDistance(new Vector(vector!))),
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };
        return await ranked.Take(30).Select(x => x.StorylineId).ToListAsync(cancellationToken);
    }

    private IQueryable<StorylineRevision> CurrentStorylines(long userId) => db.StorylineRevisions.AsNoTracking()
        .Include(x => x.Storyline).Include(x => x.Stages)
        .Include(x => x.Nodes).ThenInclude(x => x.Event).ThenInclude(x => x.SourceRevisions)
        .Include(x => x.Edges).AsSplitQuery()
        .Where(x => x.Storyline.UserId == userId && x.Storyline.DeletedAt == null && x.Revision == x.Storyline.CurrentRevision);

    public async Task<IReadOnlyList<StorylineSearchData>> ReadStorylinesAsync(long userId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var revisions = await CurrentStorylines(userId).Where(x => ids.Contains(x.StorylineId)).ToListAsync(cancellationToken);
        var indexes = await db.StorylineSearchIndexes.AsNoTracking().Where(x => x.UserId == userId && x.IsCurrent && ids.Contains(x.StorylineId))
            .ToListAsync(cancellationToken);
        return revisions.Select(r => new StorylineSearchData(r.Storyline, r,
            indexes.Single(x => x.StorylineId == r.StorylineId && x.Revision == r.Revision).RetrievalText)).ToArray();
    }

    public Task<StorylineRevision?> FindStorylineRevisionAsync(long userId, Guid id, CancellationToken cancellationToken) =>
        CurrentStorylines(userId).FirstOrDefaultAsync(x => x.StorylineId == id, cancellationToken);

    private async Task<IQueryable<EventSearchIndex>> RecordIndexesAsync(long userId, RecordSearchFilter filter, CancellationToken ct)
    {
        var visible = await RecordsAsync(userId, filter.ParticipantFriendId, ct);
        var indexes = db.EventSearchIndexes.AsNoTracking().Where(x => x.IsCurrent && visible.Any(e => e.Id == x.EventId && e.UserId == x.UserId));
        if (filter.From is { } from) indexes = indexes.Where(x => visible.Any(e => e.Id == x.EventId && (e.HappenedAt ?? e.CreatedAt) >= from));
        if (filter.To is { } to) indexes = indexes.Where(x => visible.Any(e => e.Id == x.EventId && (e.HappenedAt ?? e.CreatedAt) <= to));
        if (filter.Kind is { } kind) indexes = indexes.Where(x => visible.Any(e => e.Id == x.EventId && e.EventKind == kind));
        if (filter.Status is { } status) indexes = indexes.Where(x => visible.Any(e => e.Id == x.EventId && e.Status == status));
        if (filter.Category is { } category)
            indexes = indexes.Where(x => db.EventLabelIndexes.Any(m => m.UserId == x.UserId && m.EventId == x.EventId &&
                m.IsCurrent && m.Type == EventLabelType.PrimaryCategory && m.TaxonomyKey == category));
        if (filter.Tag is { } tag)
            indexes = indexes.Where(x => db.EventLabelIndexes.Any(m => m.UserId == x.UserId && m.EventId == x.EventId &&
                m.IsCurrent && m.Type == EventLabelType.BehaviorTag && m.TaxonomyKey == tag));
        if (filter.AdCode is { } adCode)
            indexes = indexes.Where(x => db.EventLocations.Any(l => l.UserId == x.UserId && l.EventId == x.EventId &&
                l.SourceRevision == x.SourceRevision && l.AdCode == adCode));
        if (filter.CenterLatitude is { } latitude && filter.CenterLongitude is { } longitude)
        {
            var delta = Math.Clamp(filter.RadiusMeters ?? 1000, 100, 100000) / 111_320m;
            var longitudeDelta = delta / (decimal)Math.Max(0.1, Math.Cos((double)latitude * Math.PI / 180));
            var minLat = latitude - delta; var maxLat = latitude + delta;
            var minLon = longitude - longitudeDelta; var maxLon = longitude + longitudeDelta;
            indexes = indexes.Where(x => db.EventLocations.Any(l => l.UserId == x.UserId && l.EventId == x.EventId &&
                l.SourceRevision == x.SourceRevision && l.Latitude >= minLat && l.Latitude <= maxLat && l.Longitude >= minLon && l.Longitude <= maxLon));
        }
        if (filter.Location is { } location)
            indexes = indexes.Where(x => x.SemanticRunId != null && db.SemanticMentions.Any(m => m.UserId == x.UserId &&
                m.SemanticRunId == x.SemanticRunId && m.Category == "location" && EF.Functions.TrigramsSimilarity(m.NormalizedValue, location) > 0.15));
        return indexes;
    }

    public async Task<IReadOnlyList<long>> RankRecordsAsync(long userId, RecordSearchFilter filter,
        AiSearchOrder order, float[]? vector, CancellationToken cancellationToken)
    {
        var indexes = await RecordIndexesAsync(userId, filter, cancellationToken);
        var ranked = order switch
        {
            AiSearchOrder.Recent => indexes.OrderByDescending(x => x.UpdatedAt),
            AiSearchOrder.Text => indexes.OrderByDescending(x => EF.Functions.TrigramsSimilarity(x.RetrievalText, filter.Query)).ThenByDescending(x => x.UpdatedAt),
            AiSearchOrder.Vector => indexes.Where(x => EF.Property<Vector?>(x, "Embedding") != null)
                .OrderBy(x => EF.Property<Vector>(x, "Embedding").CosineDistance(new Vector(vector!))),
            _ => throw new ArgumentOutOfRangeException(nameof(order)),
        };
        return await ranked.Take(40).Select(x => x.EventId).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecordSearchData>> ReadRecordsAsync(long userId, IReadOnlyList<long> ids, Guid? participantFriendId, CancellationToken cancellationToken)
    {
        var visible = await RecordsAsync(userId, participantFriendId, cancellationToken);
        var rows = await (from index in db.EventSearchIndexes.AsNoTracking()
                          join evt in visible.AsNoTracking() on index.EventId equals evt.Id
                          where ids.Contains(index.EventId) && index.UserId == evt.UserId && index.IsCurrent && evt.DeletedAt == null
                          select new { index, evt }).ToListAsync(cancellationToken);
        var labels = await db.EventLabelIndexes.AsNoTracking().Where(x => x.IsCurrent && ids.Contains(x.EventId) && visible.Any(e => e.Id == x.EventId && e.UserId == x.UserId)).ToListAsync(cancellationToken);
        var locations = await db.EventLocations.AsNoTracking().Where(x => ids.Contains(x.EventId) && visible.Any(e => e.Id == x.EventId && e.UserId == x.UserId)).ToListAsync(cancellationToken);
        return rows.Select(x => new RecordSearchData(x.evt, x.index, labels.Where(l => l.EventId == x.evt.Id).ToArray(),
            locations.Where(l => l.EventId == x.evt.Id).ToArray())).ToArray();
    }

    public async Task<IReadOnlyList<RecordDetailData>> ReadRecordDetailsAsync(long userId, IReadOnlyList<long> ids, CancellationToken cancellationToken)
    {
        var rows = await (from evt in ReadableEvents(userId).AsNoTracking()
                          join index in db.EventSearchIndexes.AsNoTracking() on evt.Id equals index.EventId
                          where ids.Contains(evt.Id) && index.UserId == evt.UserId && index.IsCurrent
                          select new { evt, index }).ToListAsync(cancellationToken);
        var result = new List<RecordDetailData>();
        foreach (var row in rows)
        {
            var mentions = row.index.SemanticRunId is null ? [] : await db.SemanticMentions.AsNoTracking()
                .Where(x => x.UserId == row.evt.UserId && x.SemanticRunId == row.index.SemanticRunId).Take(100).ToListAsync(cancellationToken);
            result.Add(new(row.evt, row.index, mentions));
        }
        return result;
    }

    private IQueryable<Event> StatisticsEvents(long userId, RecordStatisticsFilter filter)
    {
        var events = db.Events.AsNoTracking().Where(x => x.UserId == userId && x.DeletedAt == null);
        if (filter.From is { } from) events = events.Where(x => (x.HappenedAt ?? x.CreatedAt) >= from);
        if (filter.To is { } to) events = events.Where(x => (x.HappenedAt ?? x.CreatedAt) <= to);
        if (filter.Category is { } category) events = events.Where(e => db.EventLabelIndexes.Any(x => x.UserId == userId && x.EventId == e.Id &&
            x.IsCurrent && x.Type == EventLabelType.PrimaryCategory && x.TaxonomyKey == category));
        if (filter.Tag is { } tag) events = events.Where(e => db.EventLabelIndexes.Any(x => x.UserId == userId && x.EventId == e.Id &&
            x.IsCurrent && x.Type == EventLabelType.BehaviorTag && x.TaxonomyKey == tag));
        return events;
    }

    public Task<long> CountRecordsAsync(long userId, RecordStatisticsFilter filter, CancellationToken cancellationToken) =>
        StatisticsEvents(userId, filter).LongCountAsync(cancellationToken);

    public async Task<AmountTotal> SumExpensesAsync(long userId, RecordStatisticsFilter filter, string currency, CancellationToken cancellationToken)
    {
        var ids = StatisticsEvents(userId, filter).Select(x => new { x.Id, x.CurrentSourceRevision });
        var amounts = from expense in db.ExpenseFacts.AsNoTracking()
                      join run in db.EventSemanticRuns.AsNoTracking() on expense.SemanticRunId equals run.Id
                      join evt in ids on new { Id = run.EventId, CurrentSourceRevision = run.SourceRevision } equals new { evt.Id, evt.CurrentSourceRevision }
                      where expense.UserId == userId && run.UserId == userId && run.Status == SemanticRunStatus.Completed && expense.Currency == currency &&
                          !db.EventSemanticRuns.Any(newer => newer.EventId == run.EventId && newer.UserId == userId && newer.SourceRevision == run.SourceRevision &&
                              newer.Status == SemanticRunStatus.Completed && newer.Id > run.Id)
                      select expense.Amount;
        return await amounts.GroupBy(_ => 1).Select(g => new AmountTotal(g.Sum(), g.LongCount())).SingleOrDefaultAsync(cancellationToken) ?? new(null, 0);
    }

    public async Task<PlanCompletion> CountPlanCompletionAsync(long userId, RecordStatisticsFilter filter, CancellationToken cancellationToken)
    {
        var plans = StatisticsEvents(userId, filter).Where(x => x.EventKind == EventKind.Plan);
        var total = await plans.LongCountAsync(cancellationToken);
        return new(await plans.LongCountAsync(x => x.Status == EventStatus.Completed, cancellationToken), total);
    }

    public async Task<IReadOnlyList<MonthlyRecordCount>> CountMonthlyRecordsAsync(long userId, RecordStatisticsFilter filter, CancellationToken cancellationToken)
    {
        var rows = await StatisticsEvents(userId, filter).GroupBy(x => new { x.CreatedAt.Year, x.CreatedAt.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.LongCount() })
            .OrderBy(x => x.Year).ThenBy(x => x.Month).Take(24).ToListAsync(cancellationToken);
        return rows.Select(x => new MonthlyRecordCount(x.Year, x.Month, x.Count)).ToArray();
    }

    public async Task<IReadOnlyList<UserMemory>> SearchMemoriesAsync(long userId, string query, int limit, float[]? vector, CancellationToken cancellationToken)
    {
        var memories = db.UserMemories.AsNoTracking().Where(x => x.UserId == userId && x.Status != UserMemoryStatus.Rejected);
        var ranked = vector is not null
            ? memories.Where(x => EF.Property<Vector?>(x, "Embedding") != null).OrderBy(x => EF.Property<Vector>(x, "Embedding").CosineDistance(new Vector(vector)))
            : memories.Where(x => EF.Functions.TrigramsSimilarity(x.Content, query) > 0.1).OrderByDescending(x => x.UpdatedAt);
        var ids = await ranked.Take(limit).Select(x => x.Id).ToListAsync(cancellationToken);
        var rows = await memories.Include(x => x.Evidence).Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        return ids.Where(id => rows.Any(x => x.Id == id)).Select(id => rows.Single(x => x.Id == id)).ToArray();
    }

    public async Task<IReadOnlyList<SavedPlaceData>> SearchPlacesAsync(long userId, string query, IReadOnlyList<long> retrievedEventIds, int limit, CancellationToken cancellationToken) =>
        await (from location in db.EventLocations.AsNoTracking()
               join evt in db.Events.AsNoTracking() on location.EventId equals evt.Id
               where location.UserId == userId && evt.UserId == userId && evt.DeletedAt == null && location.UserConfirmed && location.SourceRevision == evt.CurrentSourceRevision &&
                   (query == "" || retrievedEventIds.Contains(evt.Id) || EF.Functions.TrigramsSimilarity(location.Name + " " + location.Address, query) > 0.1)
               orderby evt.HappenedAt ?? evt.CreatedAt descending
               select new SavedPlaceData(location.Id, evt.Id, evt.Title ?? "无标题", location.Name, location.Address, location.AdCode, evt.HappenedAt))
            .Take(limit).ToListAsync(cancellationToken);

    public Task<NavigationLocation?> FindNavigationLocationAsync(long userId, long locationId, CancellationToken cancellationToken) =>
        (from location in db.EventLocations.AsNoTracking()
         join evt in db.Events.AsNoTracking() on location.EventId equals evt.Id
         where location.Id == locationId && location.UserId == userId && evt.UserId == userId && evt.DeletedAt == null && location.SourceRevision == evt.CurrentSourceRevision &&
             location.UserConfirmed && location.Latitude != null && location.Longitude != null && location.CoordinateSystem == "GCJ02"
         select new NavigationLocation(evt.Id, location.Id, location.Name, location.Address, location.Latitude!.Value, location.Longitude!.Value, location.ProviderPoiId))
        .FirstOrDefaultAsync(cancellationToken);
}
