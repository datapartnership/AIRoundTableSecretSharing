export const COUNTRIES = ['IND', 'BRA', 'USA', 'GBR', 'NGA']
export const SERIES = [
  ['tokens', 'total'],
  ['token_access', 'open_source'],
  ['token_access', 'proprietary'],
  ['model_region_of_origin', 'EAS'],
  ['model_region_of_origin', 'ECS'],
  ['model_region_of_origin', 'LCN'],
  ['model_region_of_origin', 'MEA'],
  ['model_region_of_origin', 'NAC'],
  ['model_region_of_origin', 'SAS'],
  ['model_region_of_origin', 'SSF'],
]
export const MONTH_COUNT = 4
export const CELL_COUNT = COUNTRIES.length * MONTH_COUNT * SERIES.length
export const HEADERS = ['month_date', 'country_iso3', 'indicator', 'segment', 'value']
export const ERROR_DISPLAY_CAP = 20
export const LONG_MAX = 9223372036854775807n

const MONTH_RE = /^\d{4}-(0[1-9]|1[0-2])$/
const VALUE_RE = /^[0-9]+$/
const SERIES_SET = new Set(SERIES.map(([i, s]) => `${i}|${s}`))
const COUNTRY_SET = new Set(COUNTRIES)

export function epochMonths(epoch) {
  if (!epoch?.startDate) return null
  const d0 = new Date(epoch.startDate)
  if (Number.isNaN(d0.getTime())) return null
  return Array.from({ length: MONTH_COUNT }, (_, i) => {
    const d = new Date(d0)
    d.setUTCMonth(d.getUTCMonth() + i)
    return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, '0')}`
  })
}

export function isCsvFile(file) {
  if (!file) return false
  const name = (file.name || '').toLowerCase()
  if (!name.endsWith('.csv')) return false
  const type = (file.type || '').toLowerCase()
  if (!type) return true
  return (
    type === 'text/csv' ||
    type === 'application/csv' ||
    type === 'text/plain' ||
    type === 'application/vnd.ms-excel'
  )
}

function splitCsvLine(line) {
  return line.split(',').map((c) => c.trim())
}

function requiredKeys(months) {
  const keys = new Set()
  for (const country of COUNTRIES) {
    for (const month of months) {
      for (const [indicator, segment] of SERIES) {
        keys.add(`${country}|${month}|${indicator}|${segment}`)
      }
    }
  }
  return keys
}

export function parseAndValidateCsv(text, months) {
  const errors = []
  const add = (row, message) => {
    errors.push(row == null ? message : `Row ${row}: ${message}`)
  }

  if (!months || months.length !== MONTH_COUNT) {
    add(null, 'Epoch months are not available; cannot remap CSV dates')
    return { ok: false, errors, rows: [], monthMap: {}, preview: [] }
  }

  if (text == null || String(text).trim() === '') {
    add(null, 'File is empty')
    return { ok: false, errors, rows: [], monthMap: {}, preview: [] }
  }

  const rawLines = String(text).replace(/^\uFEFF/, '').split(/\r?\n/)
  while (rawLines.length > 0 && rawLines[rawLines.length - 1].trim() === '') {
    rawLines.pop()
  }

  if (rawLines.length === 0) {
    add(null, 'File is empty')
    return { ok: false, errors, rows: [], monthMap: {}, preview: [] }
  }

  const header = splitCsvLine(rawLines[0])
  if (header.length !== HEADERS.length || header.some((h, i) => h !== HEADERS[i])) {
    add(1, `Header must be exactly ${HEADERS.join(',')}`)
  }
  if (rawLines.length < 2) {
    add(null, 'File has a header but no data rows')
  }

  const parsed = []
  const seenOriginal = new Map()

  for (let i = 1; i < rawLines.length; i++) {
    const rowNum = i + 1
    const line = rawLines[i]
    if (line.trim() === '') {
      add(rowNum, 'Blank line is not allowed')
      continue
    }
    const cols = splitCsvLine(line)
    if (cols.length !== HEADERS.length) {
      add(rowNum, `Expected ${HEADERS.length} columns, found ${cols.length}`)
      continue
    }

    const [monthDate, country, indicator, segment, valueRaw] = cols
    if (!MONTH_RE.test(monthDate)) {
      add(rowNum, `month_date must be YYYY-MM (got "${monthDate}")`)
    }
    if (!COUNTRY_SET.has(country)) {
      add(rowNum, `country_iso3 must be one of ${COUNTRIES.join(', ')} (got "${country}")`)
    }
    if (!SERIES_SET.has(`${indicator}|${segment}`)) {
      add(rowNum, `Unknown indicator/segment pair "${indicator}","${segment}"`)
    }
    if (!VALUE_RE.test(valueRaw)) {
      add(rowNum, 'value must be a positive integer with no sign, decimal, or separators')
      continue
    }
    let value
    try {
      value = BigInt(valueRaw)
    } catch {
      add(rowNum, 'value is not a valid integer')
      continue
    }
    if (value === 0n) {
      add(rowNum, 'value cannot be 0')
      continue
    }
    if (value < 0n) {
      add(rowNum, 'value cannot be negative')
      continue
    }
    if (value > LONG_MAX) {
      add(rowNum, 'value exceeds the maximum 64-bit integer')
      continue
    }

    const origKey = `${country}|${monthDate}|${indicator}|${segment}`
    if (seenOriginal.has(origKey)) {
      add(rowNum, `Duplicate cell (also on row ${seenOriginal.get(origKey)})`)
      continue
    }
    seenOriginal.set(origKey, rowNum)

    parsed.push({ rowNum, monthDate, country, indicator, segment, value })
  }

  const uniqueMonths = [...new Set(parsed.map((r) => r.monthDate))].sort()
  if (parsed.length > 0 && uniqueMonths.length !== MONTH_COUNT) {
    add(null, `File must contain exactly ${MONTH_COUNT} distinct months (found ${uniqueMonths.length}: ${uniqueMonths.join(', ') || 'none'})`)
  }

  const monthMap = {}
  if (uniqueMonths.length === MONTH_COUNT) {
    uniqueMonths.forEach((csvMonth, i) => { monthMap[csvMonth] = months[i] })
  }

  const rows = parsed
    .filter((r) => monthMap[r.monthDate])
    .map((r) => ({
      country: r.country,
      month: monthMap[r.monthDate],
      csvMonth: r.monthDate,
      indicator: r.indicator,
      segment: r.segment,
      value: r.value,
      rowNum: r.rowNum,
    }))

  if (Object.keys(monthMap).length === MONTH_COUNT) {
    const required = requiredKeys(months)
    const actual = new Set(rows.map((r) => `${r.country}|${r.month}|${r.indicator}|${r.segment}`))
    const missing = [...required].filter((k) => !actual.has(k))
    const extra = [...actual].filter((k) => !required.has(k))
    if (missing.length > 0) {
      add(null, `Missing ${missing.length} required cell(s); expected ${CELL_COUNT} rows covering every country × month × series`)
    }
    if (extra.length > 0) {
      add(null, `Found ${extra.length} unexpected cell(s) that are not in the required grid`)
    }
    if (missing.length === 0 && extra.length === 0 && rows.length !== CELL_COUNT) {
      add(null, `File must contain exactly ${CELL_COUNT} data rows (found ${rows.length})`)
    }
  }

  const preview = rows.slice(0, 5).map((r) => ({
    country: r.country,
    month: r.month,
    csvMonth: r.csvMonth,
    indicator: r.indicator,
    segment: r.segment,
    value: r.value.toString(),
  }))

  return {
    ok: errors.length === 0 && rows.length === CELL_COUNT,
    errors,
    rows,
    monthMap,
    preview,
  }
}

export function formatInt(v) {
  if (v == null || v === '') return '—'
  try {
    return BigInt(v).toLocaleString()
  } catch {
    return String(v)
  }
}
