import AppKit
import Foundation

guard CommandLine.arguments.count == 2 else { fatalError("usage: generate-macos-icon.swift OUTPUT.iconset") }
let output = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)

let variants: [(String, Int)] = [
    ("icon_16x16.png", 16), ("icon_16x16@2x.png", 32),
    ("icon_32x32.png", 32), ("icon_32x32@2x.png", 64),
    ("icon_128x128.png", 128), ("icon_128x128@2x.png", 256),
    ("icon_256x256.png", 256), ("icon_256x256@2x.png", 512),
    ("icon_512x512.png", 512), ("icon_512x512@2x.png", 1024)
]

for (name, pixels) in variants {
    guard let bitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil,
        pixelsWide: pixels,
        pixelsHigh: pixels,
        bitsPerSample: 8,
        samplesPerPixel: 4,
        hasAlpha: true,
        isPlanar: false,
        colorSpaceName: .deviceRGB,
        bytesPerRow: 0,
        bitsPerPixel: 0
    ), let context = NSGraphicsContext(bitmapImageRep: bitmap) else { fatalError("Bitmap creation failed") }
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = context
    let bounds = NSRect(x: 0, y: 0, width: pixels, height: pixels)
    NSColor(red: 0.08, green: 0.075, blue: 0.11, alpha: 1).setFill()
    NSBezierPath(roundedRect: bounds.insetBy(dx: CGFloat(pixels) * 0.07, dy: CGFloat(pixels) * 0.07), xRadius: CGFloat(pixels) * 0.2, yRadius: CGFloat(pixels) * 0.2).fill()
    NSColor(red: 0.77, green: 0.71, blue: 0.99, alpha: 1).setStroke()
    let inset = CGFloat(pixels) * 0.27
    let middle = CGFloat(pixels) * 0.5
    for direction in [-1.0, 1.0] {
        let path = NSBezierPath()
        path.lineWidth = max(2, CGFloat(pixels) * 0.055)
        path.lineCapStyle = .round
        let sign = CGFloat(direction)
        path.move(to: NSPoint(x: middle + sign * inset * 0.25, y: CGFloat(pixels) * 0.72))
        path.curve(
            to: NSPoint(x: middle + sign * inset * 0.62, y: middle),
            controlPoint1: NSPoint(x: middle + sign * inset * 0.55, y: CGFloat(pixels) * 0.7),
            controlPoint2: NSPoint(x: middle + sign * inset * 0.35, y: middle + inset * 0.18)
        )
        path.curve(
            to: NSPoint(x: middle + sign * inset * 0.25, y: CGFloat(pixels) * 0.28),
            controlPoint1: NSPoint(x: middle + sign * inset * 0.35, y: middle - inset * 0.18),
            controlPoint2: NSPoint(x: middle + sign * inset * 0.55, y: CGFloat(pixels) * 0.3)
        )
        path.stroke()
    }
    context.flushGraphics()
    NSGraphicsContext.restoreGraphicsState()
    guard let png = bitmap.representation(using: .png, properties: [:]) else { fatalError("PNG generation failed") }
    try png.write(to: output.appendingPathComponent(name))
}
