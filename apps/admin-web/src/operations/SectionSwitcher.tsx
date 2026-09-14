export type SectionView = 'manage' | 'records'

export default function SectionSwitcher({
  active,
  manageLabel,
  recordsLabel,
  recordCount,
  onChange,
}: {
  active: SectionView
  manageLabel: string
  recordsLabel: string
  recordCount: number
  onChange: (view: SectionView) => void
}) {
  return <div className="section-switcher" role="group" aria-label="Choisir la partie à afficher">
    <button className={active === 'manage' ? 'selected' : undefined} aria-pressed={active === 'manage'} onClick={() => onChange('manage')} type="button">{manageLabel}</button>
    <button className={active === 'records' ? 'selected' : undefined} aria-pressed={active === 'records'} onClick={() => onChange('records')} type="button">{recordsLabel}<span>{recordCount}</span></button>
  </div>
}
