import { useLayoutEffect, useRef, useState } from 'react'

export default function MediaStage({ alt, captionText, className = '', mediaKind, src }: {
  alt: string
  captionText?: string | null
  className?: string
  mediaKind: string
  src: string
}) {
  const viewport = useRef<HTMLDivElement>(null)
  const content = useRef<HTMLSpanElement>(null)
  const [scrolling, setScrolling] = useState(false)

  useLayoutEffect(() => {
    const measure = () => setScrolling((content.current?.scrollWidth ?? 0) > (viewport.current?.clientWidth ?? 0))
    measure()
    if (!captionText || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(measure)
    if (viewport.current) observer.observe(viewport.current)
    if (content.current) observer.observe(content.current)
    return () => observer.disconnect()
  }, [captionText])

  return <div className={`media-stage ${className}`.trim()}>
    {mediaKind.toLowerCase() === 'mp4' ? <video src={src} autoPlay muted loop playsInline /> : <img src={src} alt={alt} />}
    {captionText && <div className={`media-stage-caption${scrolling ? ' is-scrolling' : ''}`} ref={viewport}>
      <div className="media-stage-caption-track"><span ref={content}>{captionText}</span>{scrolling && <span aria-hidden="true">{captionText}</span>}</div>
    </div>}
  </div>
}
