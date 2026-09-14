import type { Device } from './types'
import { formatMac } from './formatters'

export function Badge({ value }: { value: string }) {
  return <span className={`state-badge state-${value.toLowerCase()}`}>{value}</span>
}

export function NetworkAddresses({ device, kind }: { device: Device; kind: 'ip' | 'mac' }) {
  const values = device.networkInterfaces.flatMap((network) => {
    if (kind === 'mac') {
      return network.macAddress ? [{ interfaceName: network.interfaceName, value: formatMac(network.macAddress) }] : []
    }

    return network.localAddresses.map((value) => ({ interfaceName: network.interfaceName, value }))
  })

  if (values.length === 0) return <span>—</span>

  return <span className="network-values">{values.map((item) => (
    <span key={`${item.interfaceName}-${item.value}`}><code>{item.value}</code><small>{item.interfaceName}</small></span>
  ))}</span>
}
