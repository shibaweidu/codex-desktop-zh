// swift-tools-version: 5.9

import PackageDescription

let package = Package(
    name: "CodexZhLauncherMac",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "CodexZhLauncherMac", targets: ["CodexZhLauncherMac"])
    ],
    targets: [
        .target(
            name: "CLibProcBridge",
            path: "Sources/CLibProcBridge",
            publicHeadersPath: "include"
        ),
        .executableTarget(
            name: "CodexZhLauncherMac",
            dependencies: ["CLibProcBridge"],
            path: "Sources/CodexZhLauncherMac"
        ),
        .testTarget(
            name: "CodexZhLauncherMacTests",
            dependencies: ["CodexZhLauncherMac"],
            path: "Tests/CodexZhLauncherMacTests"
        )
    ]
)
