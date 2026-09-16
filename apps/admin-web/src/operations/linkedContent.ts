const linkedContentPrefix = 'display-control-url:v1\n'

export function createLinkedContent(rawUrl: string) {
  const url = new URL(rawUrl.trim())
  if (url.protocol !== 'https:' && url.protocol !== 'http:') throw new Error('Utilisez un lien HTTP ou HTTPS valide.')
  return `${linkedContentPrefix}${url.href}`
}

export function readLinkedContent(value: string) {
  if (!value.startsWith(linkedContentPrefix)) return null
  try {
    const url = new URL(value.slice(linkedContentPrefix.length).trim())
    return url.protocol === 'https:' || url.protocol === 'http:' ? url.href : null
  } catch {
    return null
  }
}
