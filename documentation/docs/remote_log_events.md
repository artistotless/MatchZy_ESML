# События MatchZy на `matchzy_remote_log_url`

MatchZy отправляет JSON-события **HTTP POST** на URL из `matchzy_remote_log_url` (также задаётся полем `remote_log_url` в JSON матча, если ваш пайплайн это поддерживает). Тело запроса: `application/json`, корневой объект содержит поле **`event`** — тип события.

Опционально можно задать заголовок авторизации:

- `matchzy_remote_log_header_key` / `matchzy_remote_log_header_value`

**Важно:** «обычные» события (`SendEventAsync`) и **realtime** (`SendRealtimeEventAsync`) используют **один и тот же** `matchzy_remote_log_url`. Realtime включается отдельно.

## Включение realtime

Консольная команда на сервере:

```text
matchzy_realtime_events_enabled 1
```

(или `true`). В JSON матча есть поле `realtime_events_enabled`, но в текущей загрузке матча из `MatchManagement.GetOptionalMatchValues` оно **не подхватывается** — надёжный способ именно cvar.

Условия отправки realtime:

| Событие | Доп. условие |
|--------|----------------|
| `round_start`, `player_death`, `player_hurt`, `bomb_*` | `matchStarted == true` |
| `player_connect`, `player_disconnect` | только `RealtimeEventsEnabled` (без проверки `matchStarted` для connect/disconnect) |

Боты и SourceTV/HLTV в realtime **не попадают** в `players` / не дают `attacker`/`victim` (обработчик возвращает `null`).

---

## Общий транспорт

| Параметр | Значение |
|----------|----------|
| Метод | `POST` |
| `Content-Type` | `application/json` |
| Идентификация матча | Поле `matchid` (строка UUID/идентификатор матча) там, где оно есть в схеме |

Сериализация через `System.Text.Json` с именами полей из атрибутов `JsonPropertyName` (ниже — как в JSON).

---

## 1. Обычные события (`SendEventAsync`)

Отправляются при настроенном непустом `matchzy_remote_log_url`. Поле **`event`** обязательно во всех типах.

### `series_start`

**Когда:** сразу после успешной загрузки матча (`LoadMatchFromJSON`), перед/вместе со стартом warmup.

| Поле | Тип | Описание |
|------|-----|----------|
| `event` | string | `"series_start"` |
| `matchid` | string | ID матча |
| `num_maps` | int | Длина серии (`num_maps` из конфига, например 3 для BO3) |
| `team1` | object | `{ "id", "name" }` |
| `team2` | object | `{ "id", "name" }` |

---

### `going_live`

**Когда:** переход в live (после готовности и старта матча), вместе со стартом записи демо.

| Поле | Тип | Описание |
|------|-----|----------|
| `event` | string | `"going_live"` |
| `matchid` | string | ID матча |
| `map_number` | int | Текущий номер карты в серии (**0-based**, как `current_map_number` в конфиге) |

---

### `map_picked`

**Когда:** вето/выбор карты — команда забрала карту в `maplist`.

| Поле | Тип | Описание |
|------|-----|----------|
| `event` | string | `"map_picked"` |
| `matchid` | string | ID матча |
| `team` | string | `"team1"` или `"team2"` |
| `map_name` | string | Имя карты |
| `map_number` | int | Порядковый номер карты в серии (**1-based**: `maplist.Count` после добавления) |

---

### `map_vetoed`

**Когда:** команда убрала карту из пула (ban).

| Поле | Тип | Описание |
|------|-----|----------|
| `event` | string | `"map_vetoed"` |
| `matchid` | string | ID матча |
| `team` | string | `"team1"` или `"team2"` |
| `map_name` | string | Имя карты |

---

### `side_picked`

**Когда:** после выбора стороны на последней карте вето.

| Поле | Тип | Описание |
|------|-----|----------|
| `event` | string | `"side_picked"` |
| `matchid` | string | ID матча |
| `team` | string | `"team1"` или `"team2"` |
| `map_name` | string | Имя карты |
| `map_number` | int | **1-based** индекс (`maplist.Count` на момент выбора) |
| `side` | string | `"ct"` или `"t"` (нижний регистр) |

---

### `round_end`

**Когда:** после окончания раунда при `isMatchLive` (live-фаза).

| Поле | Тип | Описание |
|------|-----|----------|
| `event` | string | `"round_end"` |
| `matchid` | string | ID матча |
| `map_number` | int | Текущая карта (0-based) |
| `round_number` | int | Сумма счётов команд на карте после раунда (`team1_score + team2_score`) |
| `round_time` | int | Сейчас всегда **0** в коде |
| `reason` | int | Причина конца раунда из игрового события (`EventRoundEnd.Reason`) |
| `winner` | object | `{ "side", "team" }` — `side` строка со стороной победителя (как в движке, часто `"2"` / `"3"` и т.д.), `team`: `"team1"` или `"team2"` |
| `team1` | object | Статистика команды (см. `MatchZyStatsTeam`) |
| `team2` | object | Статистика команды |

**Объект команды в `round_end` / `map_result`** (`MatchZyStatsTeam`):

| Поле | Тип | Описание |
|------|-----|----------|
| `id` | string | ID команды |
| `name` | string | Имя команды |
| `series_score` | int | В **`round_end`** в коде передаётся **0** для обеих команд (серия не дублируется в этом событии) |
| `score` | int | Очки на текущей карте |
| `score_ct` | int | В текущей реализации для этого события **0** |
| `score_t` | int | В текущей реализации для этого события **0** |
| `players` | array | Список игроков со статистикой |

**Игрок** (`StatsPlayer`):

| Поле | Тип |
|------|-----|
| `steamid` | string |
| `name` | string |
| `stats` | object `PlayerStats` |

**`PlayerStats`** (основные поля JSON): `kills`, `deaths`, `assists`, `flash_assists`, `team_kills`, `suicides`, `damage`, `utility_damage`, `enemies_flashed`, `friendlies_flashed`, `knife_kills`, `headshot_kills`, `rounds_played`, `bomb_defuses`, `bomb_plants`, `1k`…`5k`, `1v1`…`1v5`, `first_kills_t`, `first_kills_ct`, `first_deaths_t`, `first_deaths_ct`, `trade_kills`, `kast`, `score`, `mvp`.

---

### `map_result`

**Когда:** окончание карты (`HandleMatchEnd`), до логики следующей карты / `series_end`.

| Поле | Тип | Описание |
|------|-----|----------|
| `event` | string | `"map_result"` |
| `matchid` | string | ID матча |
| `map_number` | int | Завершённая карта (0-based) |
| `winner` | object | `{ "side", "team" }` по итогам карты |
| `team1` | object | `series_score` — **реальный** счёт серии team1; `players` в коде передаётся как **пустой массив** |
| `team2` | object | Аналогично team1 |

---

### `series_end`

**Когда:** завершение всей серии (`EndSeries`) — клинч, исчерпание карт или ничья по правилам серии.

| Поле | Тип | Описание |
|------|-----|----------|
| `event` | string | `"series_end"` |
| `matchid` | string | ID матча |
| `time_until_restore` | int | Секунды до восстановления (в коде **10**) |
| `winner` | object | `{ "side", "team" }`; при ничьей `team` может быть `"none"` |
| `team1_series_score` | int | Итог серии |
| `team2_series_score` | int | Итог серии |

Отправка идёт в фоне с задержкой **2 с** после начала обновления БД, чтобы условно успел уйти `map_result`.

---

## 2. Realtime-события (`SendRealtimeEventAsync`)

Тот же URL и заголовки. Поле **`event`** — имя типа ниже.

### `round_start`

| Поле | Тип | Описание |
|------|-----|----------|
| `event` | string | `"round_start"` |
| `matchid` | string | ID матча |
| `map` | string | `Server.MapName` |
| `map_number` | int | Текущая карта (0-based) |
| `round_number` | int | `team1_score + team2_score` на момент события |
| `team1_score` | int | Счёт на карте |
| `team2_score` | int | Счёт на карте |
| `players` | array | Снимок всех живых игроков (без ботов/HLTV) |

**Элемент `players` (`RealtimePlayerInfo`):**

| Поле | Тип |
|------|-----|
| `steamid` | string |
| `name` | string |
| `team` | string | `"CT"`, `"T"` или `"Spectator"` |
| `alive` | bool |
| `hp` | int |
| `armor` | int |
| `money` | int |
| `kills` | int |
| `deaths` | int |
| `assists` | int |

---

### `player_death`

| Поле | Тип | Описание |
|------|-----|----------|
| `event` | string | `"player_death"` |
| `matchid` | string | |
| `map` | string | |
| `map_number` | int | |
| `round_number` | int | Сумма счётов на момент смерти |
| `attacker` | object? | `RealtimePlayerInfo` или отсутствует (world / нет данных) |
| `victim` | object | `RealtimePlayerInfo` |
| `weapon` | string | Имя оружия из игрового события |
| `headshot` | bool | |
| `thru_smoke` | bool | |
| `blind` | bool | `Attackerblind` из события |

---

### `player_hurt`

| Поле | Тип | Описание |
|------|-----|----------|
| `event` | string | `"player_hurt"` |
| `matchid` | string | |
| `map` | string | |
| `map_number` | int | |
| `round_number` | int | |
| `attacker` | object? | |
| `victim` | object | |
| `weapon` | string | |
| `dmg_health` | int | |
| `dmg_armor` | int | |
| `hitgroup` | int | Hitgroup движка (как в `EventPlayerHurt`) |

---

### `bomb_planted` / `bomb_defused` / `bomb_exploded`

Общая форма (`RealtimeBombEvent`), меняется только **`event`**:

| Поле | Тип | Описание |
|------|-----|----------|
| `event` | string | Одно из: `bomb_planted`, `bomb_defused`, `bomb_exploded` |
| `matchid` | string | |
| `map` | string | |
| `map_number` | int | |
| `round_number` | int | |
| `player` | object | `RealtimePlayerInfo` игрока |
| `site` | string | `"A"` или `"B"` |

**Особенность `bomb_exploded`:** в обработчике нет игрока в API события — подставляется **заглушка**: `steamid: "0"`, пустое имя, `team: "T"`, нули по статам.

---

### `player_connect` / `player_disconnect`

Используется один класс с разным значением **`event`**.

| Поле | Тип | Описание |
|------|-----|----------|
| `event` | string | `"player_connect"` или `"player_disconnect"` |
| `matchid` | string | |
| `map` | string | |
| `player` | object | `RealtimePlayerInfo` |

Для `player_connect` / `player_disconnect` **нет** полей `map_number` и `round_number` в payload.

---

## 3. Типы в коде без отправки на `remote_log_url`

В `Events.cs` объявлены `MatchZyPlayerDisconnectedEvent` (`player_disconnect` в старом смысле Get5) и `MatchZyDemoUploadedEvent` (`demo_upload_ended`). В текущей кодовой базе **нет** вызовов `SendEventAsync` для них — на webhook они **не уходят**. Отдельно настраиваются `matchzy_demo_upload_url` / S3 URL и notify URL для демо.

---

## 4. Краткая таблица всех `event`, реально уходящих на URL

| `event` | Канал |
|---------|--------|
| `series_start` | Обычный |
| `going_live` | Обычный |
| `map_picked` | Обычный |
| `map_vetoed` | Обычный |
| `side_picked` | Обычный |
| `round_end` | Обычный |
| `map_result` | Обычный |
| `series_end` | Обычный |
| `round_start` | Realtime |
| `player_death` | Realtime |
| `player_hurt` | Realtime |
| `bomb_planted` | Realtime |
| `bomb_defused` | Realtime |
| `bomb_exploded` | Realtime |
| `player_connect` | Realtime |
| `player_disconnect` | Realtime |

---

## Ссылки на исходники

- Модели и имена JSON: `Events.cs`, статистика команд/игроков: `MatchData.cs`
- Обычная отправка: `PublishEvents.cs` → `SendEventAsync`
- Realtime: `RealtimePublishEvents.cs` → `SendRealtimeEventAsync`; хуки: `EventHandlers.cs`
- Точки вызова: `MatchManagement.cs` (`series_start`, `series_end`), `Utility.cs` (`going_live`, `round_end`, `map_result`), `MapVeto.cs` (карты/стороны)

Дополнительно может существовать OpenAPI-описание в `event_schema.yml` — сверяйте при интеграции, если схема обновлялась независимо от кода.
