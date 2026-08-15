/*
  The history page.

  Draws one DashboardDocument, from whichever of the two places it comes:

    - inlined into the page, when `--history --format html` wrote a file
    - fetched from /api/v1/dashboard, when `tabbit --serve` is running

  Same object, same renderer. That is the point: an offline copy somebody mailed round
  and the live page cannot report different numbers, because neither computes any.

  No dependencies. The charts are hand-drawn SVG - two line charts and a bar chart is
  not worth a library, and a library would need a CDN this cannot reach: the tool is
  expected to run on closed networks.
*/

(() => {
  'use strict';

  const $ = (sel, root = document) => root.querySelector(sel);

  const el = (tag, attrs = {}, ...children) => {
    const node = document.createElementNS(
      tag === 'svg' || SVG_TAGS.has(tag) ? 'http://www.w3.org/2000/svg' : 'http://www.w3.org/1999/xhtml',
      tag);

    for (const [key, value] of Object.entries(attrs)) {
      if (value === null || value === undefined || value === false) continue;
      if (key === 'class') node.setAttribute('class', value);
      else if (key === 'text') node.textContent = value;
      else node.setAttribute(key, value);
    }

    for (const child of children.flat()) {
      if (child === null || child === undefined || child === false) continue;
      node.appendChild(typeof child === 'string' ? document.createTextNode(child) : child);
    }

    return node;
  };

  const SVG_TAGS = new Set(['g', 'path', 'rect', 'circle', 'line', 'text', 'polyline']);

  const num = value => (value === null || value === undefined) ? '—' : value.toLocaleString();

  const shortDate = iso => {
    if (!iso) return '';
    const at = new Date(iso);
    return Number.isNaN(at.valueOf()) ? iso : at.toLocaleString(undefined, {
      year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit',
    });
  };

  // --------------------------------------------------------------------- charts

  const CHART = { w: 720, h: 160, top: 12, right: 12, bottom: 22, left: 48 };

  /**
   * A line for a value over snapshots.
   *
   * One series, so no legend: the card's own heading names it. A crosshair and a tooltip
   * come as standard - the chart is on a screen, and "which commit was that spike" is the
   * first thing anyone asks of it.
   */
  function lineChart(points, label) {
    if (!points || points.length === 0) return el('p', { class: 'empty', text: 'No snapshots yet.' });

    const { w, h, top, right, bottom, left } = CHART;
    const innerW = w - left - right;
    const innerH = h - top - bottom;

    const peak = Math.max(1, ...points.map(p => p.value));
    const marks = ticks(peak);

    // The domain runs to the top gridline rather than to the highest value: a line drawn
    // along the frame reads as clipped, and a reader cannot tell whether it went off.
    const max = Math.max(peak, marks[marks.length - 1]);

    const x = i => left + (points.length === 1 ? innerW / 2 : (i / (points.length - 1)) * innerW);
    const y = v => top + innerH - (v / max) * innerH;

    const svg = el('svg', { viewBox: `0 0 ${w} ${h}`, role: 'img', 'aria-label': label });

    for (const value of marks) {
      svg.appendChild(el('line', {
        class: 'grid-line', x1: left, x2: left + innerW, y1: y(value), y2: y(value),
      }));

      svg.appendChild(el('text', {
        class: 'tick', x: left - 6, y: y(value) + 3, 'text-anchor': 'end', text: num(value),
      }));
    }

    svg.appendChild(el('line', {
      class: 'axis-line', x1: left, x2: left + innerW, y1: top + innerH, y2: top + innerH,
    }));

    svg.appendChild(el('polyline', {
      class: 'series-line',
      points: points.map((p, i) => `${x(i)},${y(p.value)}`).join(' '),
    }));

    // Markers only when they are not going to collide into a smear.
    if (points.length <= 40) {
      points.forEach((p, i) => svg.appendChild(el('circle', {
        class: 'series-dot', cx: x(i), cy: y(p.value), r: 4,
      })));
    }

    const crosshair = el('line', { class: 'crosshair', y1: top, y2: top + innerH, opacity: 0 });
    svg.appendChild(crosshair);

    points.forEach((p, i) => {
      const half = points.length === 1 ? innerW / 2 : innerW / (points.length - 1) / 2;

      const hit = el('rect', {
        class: 'hit', x: x(i) - half, y: top, width: Math.max(8, half * 2), height: innerH,
      });

      hit.addEventListener('pointerenter', event => {
        crosshair.setAttribute('x1', x(i));
        crosshair.setAttribute('x2', x(i));
        crosshair.setAttribute('opacity', 1);

        showTip(event, [
          [label, num(p.value)],
          ['commit', p.shortCommit],
          ['when', shortDate(p.committedAt)],
        ]);
      });

      hit.addEventListener('pointerleave', () => {
        crosshair.setAttribute('opacity', 0);
        hideTip();
      });

      svg.appendChild(hit);
    });

    svg.appendChild(el('text', {
      class: 'tick', x: left, y: h - 6, text: points[0].shortCommit || '',
    }));

    if (points.length > 1) {
      svg.appendChild(el('text', {
        class: 'tick', x: left + innerW, y: h - 6, 'text-anchor': 'end',
        text: points[points.length - 1].shortCommit || '',
      }));
    }

    return svg;
  }

  /** Horizontal bars for magnitude by person. Long names, so they run across. */
  function barChart(rows, label) {
    if (!rows || rows.length === 0) return el('p', { class: 'empty', text: 'Nobody yet.' });

    const rowH = 26;
    const left = 150;
    const w = 720;
    const h = rows.length * rowH + 8;
    const max = Math.max(1, ...rows.map(r => r.value));

    const svg = el('svg', { viewBox: `0 0 ${w} ${h}`, role: 'img', 'aria-label': label });

    rows.forEach((row, i) => {
      const y = i * rowH + 4;
      const width = Math.max(2, (row.value / max) * (w - left - 60));

      svg.appendChild(el('text', {
        class: 'tick', x: left - 8, y: y + 13, 'text-anchor': 'end', text: row.name,
      }));

      // 4px rounded ends, anchored to the baseline at the left.
      svg.appendChild(el('rect', {
        class: 'series-bar', x: left, y, width, height: rowH - 8, rx: 4,
      }));

      svg.appendChild(el('text', {
        class: 'tick', x: left + width + 6, y: y + 13, text: num(row.value),
      }));

      const hit = el('rect', { class: 'hit', x: 0, y, width: w, height: rowH - 4 });

      hit.addEventListener('pointerenter', event => showTip(event, row.detail));
      hit.addEventListener('pointerleave', hideTip);

      svg.appendChild(hit);
    });

    return svg;
  }

  function ticks(max) {
    const step = Math.pow(10, Math.floor(Math.log10(max || 1)));
    const nice = max / step > 5 ? step * 2 : max / step > 2 ? step : step / 2;

    const values = [];
    // Past the peak by one step, so the plot has a gridline above the data.
    for (let v = 0; v < max + nice; v += nice) values.push(Math.round(v));

    return values.length > 6 ? values.filter((_, i) => i % 2 === 0) : values;
  }

  // -------------------------------------------------------------------- tooltip

  let tip = null;

  function showTip(event, entries) {
    if (!entries || entries.length === 0) return;

    if (!tip) {
      tip = el('div', { class: 'tooltip' });
      document.body.appendChild(tip);
    }

    tip.textContent = '';

    for (const [key, value] of entries) {
      if (value === null || value === undefined || value === '') continue;

      tip.appendChild(el('div', {},
        el('span', { class: 'k', text: key + ' ' }),
        el('span', { text: String(value) })));
    }

    tip.style.display = 'block';
    tip.style.left = Math.min(event.clientX + 14, window.innerWidth - 340) + 'px';
    tip.style.top = (event.clientY + 14) + 'px';
  }

  function hideTip() {
    if (tip) tip.style.display = 'none';
  }

  // --------------------------------------------------------------------- pieces

  function tile(label, value, note) {
    return el('div', { class: 'tile' },
      el('div', { class: 'label', text: label }),
      el('div', { class: 'value', text: value }),
      note ? el('div', { class: 'note', text: note }) : null);
  }

  const GLYPH = { Added: '+', Removed: '−', Modified: '~' };

  /** A value, or a plain statement that there was none - never an empty gap. */
  function value(text) {
    return text === null || text === undefined
      ? el('span', { class: 'blank', text: '(blank)' })
      : el('span', { text: text.length > 120 ? text.slice(0, 120) + '…' : text });
  }

  function transition(before, after, kind) {
    if (kind === 'Added') return el('span', { class: 'now' }, value(after));
    if (kind === 'Removed') return el('span', { class: 'was' }, value(before));

    return el('span', {}, el('span', { class: 'was' }, value(before)), ' → ',
                          el('span', { class: 'now' }, value(after)));
  }

  function place(location) {
    if (!location || !location.sheet) return null;

    const label = `${location.sheet}!${location.cell}`;

    return location.url
      ? el('a', { class: 'at', href: location.url, target: '_blank', rel: 'noreferrer', text: label })
      : el('span', { class: 'at', text: `${location.file} : ${label}` });
  }

  /**
   * A column's attributes, as a phrase.
   *
   * They are stored as JSON so a reader of the history can pick them apart; printing the
   * JSON back is not picking them apart. Anything unrecognised is shown rather than
   * dropped - an attribute this does not know about is still a change somebody made.
   */
  function descriptor(text) {
    if (text === null || text === undefined) return null;

    let parsed;

    try {
      parsed = JSON.parse(text);
    } catch {
      return el('span', { text });
    }

    if (!parsed || typeof parsed !== 'object') return el('span', { text: String(text) });

    const bits = [];

    if (parsed.type) bits.push(parsed.type);
    if (parsed.side === 'c') bits.push('client only');
    if (parsed.side === 's') bits.push('server only');

    if (parsed.refTable)
      bits.push('-> ' + parsed.refTable + (parsed.refField ? '.' + parsed.refField : ''));

    const known = new Set(['type', 'side', 'refTable', 'refField', 'comment']);

    for (const [key, item] of Object.entries(parsed))
      if (!known.has(key)) bits.push(key + ' ' + item);

    // The comment goes in the tooltip: it is prose, and the longest thing here by far.
    return el('span', { title: parsed.comment || '' }, bits.join(' · ') || String(text));
  }

  function schemaTransition(before, after, kind) {
    if (kind === 'Added') return el('span', { class: 'now' }, descriptor(after));
    if (kind === 'Removed') return el('span', { class: 'was' }, descriptor(before));

    return el('span', {}, el('span', { class: 'was' }, descriptor(before)), ' -> ',
                          el('span', { class: 'now' }, descriptor(after)));
  }

  /**
   * What shipping a change set requires, as chips: `data`, `code`, or both.
   *
   * The reasons ride in the tooltip and the warnings are rendered by the caller -
   * a chip answers at a glance, and the glance is the point. Everything is computed
   * server-side; this only draws it, so the terminal and the page cannot disagree.
   */
  function shipChips(advice) {
    if (!advice) return null;

    const chips = el('span', { class: 'ship', title: (advice.reasons || []).join('\n') });

    if (advice.data) chips.appendChild(el('span', { class: 'chip data', text: 'data' }));
    if (advice.code) chips.appendChild(el('span', { class: 'chip code', text: 'code' }));

    if (!advice.data && !advice.code)
      chips.appendChild(el('span', { class: 'chip none', text: 'nothing to ship' }));

    return chips;
  }

  function change(kind, what, detail, location) {
    return el('div', { class: 'change ' + kind.toLowerCase() },
      // The glyph and the word carry the meaning; the colour only reinforces it.
      el('span', { class: 'glyph', text: GLYPH[kind] || '?', title: kind }),
      el('span', { class: 'what' }, what, detail ? el('span', {}, '  ', detail) : null),
      place(location) || el('span', { class: 'kind', text: kind }));
  }

  // ---------------------------------------------------------------------- render

  function render(data) {
    const root = $('#app');
    root.textContent = '';

    $('#title').textContent = `${data.project} / ${data.branch}`;

    renderTiles(root, data);
    renderCharts(root, data);
    renderTimeline(root, data);
    renderAuthors(root, data);
  }

  function renderTiles(root, data) {
    const totals = data.stats && data.stats.data ? data.stats.data.totals : null;
    const history = data.history ? data.history.totals : null;

    const tiles = el('div', { class: 'tiles' });

    if (totals) {
      tiles.appendChild(tile('tables', num(totals.tables)));
      tiles.appendChild(tile('rows', num(totals.rows)));
      tiles.appendChild(tile('columns', num(totals.fields)));
      tiles.appendChild(tile('cells', num(totals.cells), `${num(totals.emptyCells)} blank`));
    }

    if (history) {
      tiles.appendChild(tile('snapshots', num(history.snapshots), 'in this range'));
      tiles.appendChild(tile('cells changed', num(history.cells)));
    }

    root.appendChild(tiles);
  }

  function renderCharts(root, data) {
    const grid = el('div', { class: 'grid-2' });

    grid.appendChild(el('section', { class: 'card' },
      el('h2', { text: 'Rows over time' }),
      el('p', { class: 'caption', text: 'One point per recorded snapshot, oldest on the left.' }),
      lineChart(data.rows, 'rows')));

    grid.appendChild(el('section', { class: 'card' },
      el('h2', { text: 'Cells changed per snapshot' }),
      el('p', { class: 'caption', text: 'How much each conversion moved.' }),
      lineChart(data.churn, 'cells changed')));

    root.appendChild(grid);
  }

  function renderTimeline(root, data) {
    const history = data.history;

    const card = el('section', { class: 'card' },
      el('h2', { text: 'Changes' }),
      el('p', {
        class: 'caption',
        text: `${history.query.from || '(start)'} … ${history.query.to || '(head)'}`
              + `  ·  ${num(history.totals.schema)} schema, ${num(history.totals.rows)} row,`
              + ` ${num(history.totals.cells)} cell`,
      }));

    // The range's verdict, right under the heading: "to go from A to B, what do I
    // deploy?" is the question this card gets opened for during live operations.
    if (history.deployment) {
      const advice = history.deployment;

      card.appendChild(el('div', { class: 'ship-line' },
        el('span', { class: 'ship-label', text: 'to ship this range' }),
        shipChips(advice),
        (advice.reasons && advice.reasons.length)
          ? el('span', { class: 'ship-why', text: advice.reasons.join('  ·  ') })
          : null));

      for (const warning of advice.warnings || [])
        card.appendChild(el('div', { class: 'warn', text: warning }));
    }

    // Anything the answer did that was not asked for - a tag resolved, a commit with no
    // snapshot stood in for. It changes what the numbers describe, so it is on the page.
    for (const note of history.query.notes || []) {
      card.appendChild(el('div', { class: 'warn', text: note.replace(/`/g, '') }));
    }

    if (history.query.truncated) {
      // Said on the page, not only in the JSON. A cut list that does not admit it reads
      // as a complete one.
      card.appendChild(el('div', {
        class: 'warn',
        text: `${num(history.query.omitted)} further change(s) are not shown - the limit was `
              + `${num(history.query.limit)}. Narrow the range, or filter by table.`,
      }));
    }

    if (history.snapshots.length === 0) {
      card.appendChild(el('p', { class: 'empty', text: 'Nothing changed in this range.' }));
      root.appendChild(card);
      return;
    }

    // Newest first on the page: the question is almost always about a recent change.
    for (const snapshot of [...history.snapshots].reverse())
      card.appendChild(renderSnapshot(snapshot));

    root.appendChild(card);
  }

  function renderSnapshot(snapshot) {
    const node = el('div', { class: 'snapshot' },
      el('div', { class: 'who' },
        el('span', { class: 'hash', text: snapshot.shortCommit }),
        el('strong', { text: snapshot.authorName || '(unknown author)' }),
        el('span', { class: 'when', text: shortDate(snapshot.committedAt) }),
        shipChips(snapshot.deployment)));

    if (snapshot.subject)
      node.appendChild(el('div', { class: 'subject', text: snapshot.subject }));

    // The quiet failures: an enum renumbered, a label removed, a table gone. Nothing
    // rejects these, so the page is where they get said.
    for (const warning of (snapshot.deployment && snapshot.deployment.warnings) || [])
      node.appendChild(el('div', { class: 'warn', text: warning }));

    if (!snapshot.attributable) {
      node.appendChild(el('div', {
        class: 'warn',
        text: snapshot.dirty
          ? 'Recorded from a working copy with uncommitted changes, so these edits are not '
            + 'this commit author’s to claim.'
          : 'Nothing identified this conversion, so these edits cannot be attributed.',
      }));
    }

    if (snapshot.pruned) {
      node.appendChild(el('div', {
        class: 'warn',
        text: 'This snapshot’s change detail was pruned to reclaim space. Its statistics are '
              + 'still here; what it changed is no longer recorded.',
      }));
    }

    if (!snapshot.followsParent && snapshot.previousCommit) {
      node.appendChild(el('div', {
        class: 'warn',
        text: `Measured from ${snapshot.previousCommit.slice(0, 12)}, which is not this commit’s `
              + 'parent — the commits in between were never converted, so these changes cover '
              + 'more than this one.',
      }));
    }

    // A renamed column moves every one of its cells, which is not an edit anybody made.
    // Those are folded into the rename's own line rather than listed.
    const renamed = new Set();

    for (const item of snapshot.schema) {
      if (!item.renamedFrom) continue;

      renamed.add(JSON.stringify([item.entity, item.renamedFrom]));
      renamed.add(JSON.stringify([item.entity, item.member]));
    }

    for (const item of snapshot.schema) {
      const what = item.member ? `${item.entity}.${item.member}` : item.entity;

      if (item.renamedFrom) {
        const carried = snapshot.cells.filter(
          c => c.table === item.entity && c.field === item.member).length;

        node.appendChild(change('Modified',
          `field ${item.entity}.${item.renamedFrom} -> ${item.member}`,
          el('span', {},
            el('span', { class: 'kind', text: 'renamed' }),
            carried ? el('span', { class: 'kind', text: `  ${num(carried)} rows carried over` }) : null),
          item.location));

        continue;
      }

      const shape = item.entityKind === 'Field'
        ? schemaTransition(item.before, item.after, item.kind)
        : (item.before || item.after ? transition(item.before, item.after, item.kind) : null);

      node.appendChild(change(
        item.kind, `${item.entityKind.toLowerCase()} ${what}`, shape, item.location));
    }

    for (const item of snapshot.cells) {
      if (renamed.has(JSON.stringify([item.table, item.field]))) continue;

      node.appendChild(change(item.kind, `${item.table}[${item.rowKey}].${item.field}`,
        transition(item.before, item.after, item.kind), item.location));
    }

    // A row line only where no cell of that row is already listed - otherwise every
    // edited row would be said twice.
    const named = new Set(snapshot.cells.map(c => JSON.stringify([c.table, c.rowKey])));

    for (const item of snapshot.rows) {
      if (named.has(JSON.stringify([item.table, item.rowKey]))) continue;

      node.appendChild(change(item.kind, `${item.table}[${item.rowKey}]`, null, null));
    }

    // Said rather than left blank. A commit that touched something other than the sheets
    // still gets a snapshot, and an entry with an empty space under it reads as a page
    // that failed to draw.
    if (!snapshot.pruned
        && snapshot.counts.schema + snapshot.counts.rows + snapshot.counts.cells === 0) {
      node.appendChild(el('div', { class: 'empty', text: 'No change to the sheets.' }));
    }

    return node;
  }

  function renderAuthors(root, data) {
    if (!data.authors || data.authors.length === 0) return;

    const rows = data.authors.map(a => ({
      name: a.name,
      value: a.cells,
      detail: [
        ['cells', num(a.cells)],
        ['rows', num(a.rows)],
        ['schema', num(a.schema)],
        ['snapshots', num(a.snapshots)],
        ['last', shortDate(a.lastAt)],
      ],
    }));

    root.appendChild(el('section', { class: 'card' },
      el('h2', { text: 'Cells changed by person' }),
      el('p', { class: 'caption', text: 'Over the range above.' }),
      barChart(rows, 'cells changed')));
  }

  // ------------------------------------------------------------------- start-up

  function inlined() {
    const node = document.getElementById('data');
    return node ? JSON.parse(node.textContent) : null;
  }

  async function live() {
    const params = new URLSearchParams(window.location.search);

    const response = await fetch('api/v1/dashboard?' + params.toString(), {
      headers: { 'Accept': 'application/json' },
    });

    if (!response.ok) {
      throw new Error(`${response.status} ${response.statusText}: ${(await response.text()).slice(0, 200)}`);
    }

    return response.json();
  }

  function themeToggle() {
    const button = $('#theme');
    if (!button) return;

    button.addEventListener('click', () => {
      const dark = document.documentElement.getAttribute('data-theme') === 'dark'
        || (!document.documentElement.hasAttribute('data-theme')
            && window.matchMedia('(prefers-color-scheme: dark)').matches);

      document.documentElement.setAttribute('data-theme', dark ? 'light' : 'dark');
    });
  }

  async function start() {
    themeToggle();

    try {
      const data = inlined() || await live();
      render(data);
    } catch (error) {
      $('#app').textContent = '';
      $('#app').appendChild(el('div', { class: 'card' },
        el('h2', { text: 'The history could not be read' }),
        el('p', { class: 'caption', text: String(error && error.message || error) })));
    }
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
