// 시간 표기: 26.11.05 14:46 양식 · 「n분 전」 · 내일 n시

import { TOMORROW_HOUR, TZ_OFFSET_MIN } from './config'

const pad = (n: number) => String(n).padStart(2, '0')

/** 그 시각의 시간대 차(ms). 설정이 없으면 이 PC 의 시간대 */
function offsetMs(ms: number): number {
  const min = TZ_OFFSET_MIN ?? -new Date(ms).getTimezoneOffset()
  return min * 60_000
}

/** 26.11.05 14:46 */
export function clock(ms: number): string {
  const d = new Date(ms + offsetMs(ms))
  const ymd = `${pad(d.getUTCFullYear() % 100)}.${pad(d.getUTCMonth() + 1)}.${pad(d.getUTCDate())}`
  return `${ymd} ${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`
}

/** 14:46:03 */
export function timeOnly(ms: number): string {
  const d = new Date(ms + offsetMs(ms))
  return `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}:${pad(d.getUTCSeconds())}`
}

/** 방금 · n분 전 · n시간 전 · n일 전 */
export function ago(ms: number, now: number): string {
  const min = Math.floor((now - ms) / 60_000)
  if (min < 1) return '방금'
  if (min < 60) return `${min}분 전`
  const hr = Math.floor(min / 60)
  if (hr < 24) return `${hr}시간 전`
  return `${Math.floor(hr / 24)}일 전`
}

/** 내일 TOMORROW_HOUR 시(그 시간대 기준)의 시각(ms) */
export function tomorrowAt(now: number): number {
  const off = offsetMs(now)
  const d = new Date(now + off)
  d.setUTCDate(d.getUTCDate() + 1)
  d.setUTCHours(TOMORROW_HOUR, 0, 0, 0)
  return d.getTime() - off
}
