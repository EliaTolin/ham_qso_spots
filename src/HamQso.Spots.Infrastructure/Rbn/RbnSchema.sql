-- F71: the RBN node list (last good read) and the skimmers' activity per minute
-- (48 hours, deleted by the Host every hour). Idempotent: run at every start.
create table if not exists rbn_nodes (
    call          text        primary key,
    locator       text,
    bands         text[]      not null default '{}',
    online        boolean     not null,
    last_seen_at  timestamptz,
    fetched_at    timestamptz not null
);

create table if not exists rbn_skimmer_activity (
    skimmer       text        not null,
    band          text        not null,
    minute        timestamptz not null,
    spots         integer     not null,
    last_spot_at  timestamptz not null,
    primary key (band, minute, skimmer)
);

create index if not exists rbn_skimmer_activity_minute on rbn_skimmer_activity (minute);
