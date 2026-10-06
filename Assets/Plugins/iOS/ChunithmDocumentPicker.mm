#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>
#if __has_include(<os/proc.h>)
#import <os/proc.h>
#endif

// iOS 文件选择器桥接层。
//
// Unity 侧只知道三种模式和一组回调：zip、skin、images。这里负责处理
// UIDocumentPicker 的安全作用域，并把文件复制到 Caches/Imports 后再通知
// Unity。复制采用流式读写，歌曲 ZIP 和皮肤包不会因为一次性读入 NSData
// 而制造额外的大块内存峰值。

static NSString * const kUnityObjectName = @"CHUNITHM Mobile Runtime";
static NSString * const kCacheFolderName = @"Imports";

extern "C" void UnitySendMessage(const char *objectName, const char *methodName, const char *message);

@interface ChunithmDocumentPickerDelegate : NSObject <UIDocumentPickerDelegate>
@property(nonatomic, copy) NSString *mode;
@end

static ChunithmDocumentPickerDelegate *gPickerDelegate;

static void SendUnityMessage(NSString *method, NSString *value) {
    if (method.length == 0) return;
    NSString *safeValue = value ?: @"";
    UnitySendMessage(kUnityObjectName.UTF8String, method.UTF8String, safeValue.UTF8String);
}

static NSString *CacheImportsDirectory(void) {
    NSArray<NSString *> *paths = NSSearchPathForDirectoriesInDomains(
        NSCachesDirectory, NSUserDomainMask, YES);
    NSString *root = paths.firstObject ?: NSTemporaryDirectory();
    NSString *directory = [root stringByAppendingPathComponent:kCacheFolderName];
    [[NSFileManager defaultManager] createDirectoryAtPath:directory
                              withIntermediateDirectories:YES
                                               attributes:nil
                                                    error:nil];
    return directory;
}

static NSString *SafeFileName(NSString *name, NSString *fallback) {
    NSString *candidate = name.length > 0 ? name : fallback;
    NSCharacterSet *unsafe = [NSCharacterSet characterSetWithCharactersInString:@"/\\:\n\r|"];
    candidate = [[candidate componentsSeparatedByCharactersInSet:unsafe]
        componentsJoinedByString:@"_"];
    return candidate.length > 0 ? candidate : fallback;
}

static long long DocumentSize(NSURL *url) {
    NSNumber *size = nil;
    if ([url getResourceValue:&size forKey:NSURLFileSizeKey error:nil] && size != nil)
        return size.longLongValue;
    NSDictionary *attributes = [[NSFileManager defaultManager] attributesOfItemAtPath:url.path error:nil];
    return [attributes[NSFileSize] longLongValue];
}

// File providers may return a security-scoped URL before the item has been
// materialized locally.  In that state opening an NSInputStream can return 0
// temporarily, which is not the same as end-of-file.  Ask iCloud to download
// the item and wait for a stable local copy before starting the Unity import.
static BOOL WaitForDocumentAvailability(NSURL *url, NSError **error) {
    if (url == nil) {
        if (error) *error = [NSError errorWithDomain:@"CHUNITHMImport"
                                                  code:20
                                              userInfo:@{NSLocalizedDescriptionKey:
                                                         @"没有找到可读取的文件"}];
        return NO;
    }

    NSFileManager *fileManager = [NSFileManager defaultManager];
    BOOL ubiquitous = NO;
    if (@available(iOS 11.0, *)) {
        NSNumber *isUbiquitous = nil;
        [url getResourceValue:&isUbiquitous forKey:NSURLIsUbiquitousItemKey error:nil];
        ubiquitous = isUbiquitous.boolValue;
        if (ubiquitous) {
            NSNumber *isDownloaded = nil;
            [url getResourceValue:&isDownloaded forKey:NSURLUbiquitousItemIsDownloadedKey error:nil];
            if (!isDownloaded.boolValue)
                [fileManager startDownloadingUbiquitousItemAtURL:url error:nil];
        }
    }
    if (!ubiquitous && [url isFileURL] && ![fileManager fileExistsAtPath:url.path]) {
        if (error) *error = [NSError errorWithDomain:@"CHUNITHMImport"
                                                  code:21
                                              userInfo:@{NSLocalizedDescriptionKey:
                                                         @"所选文件尚未完成下载，请等待 Files 中的状态结束后重试"}];
        return NO;
    }
    if (!ubiquitous) return YES;

    // A provider is allowed to need a short period to finish materializing an
    // item.  Do not wait forever: the Unity overlay can then show a useful
    // retry message instead of remaining on “正在读取” indefinitely.
    const NSTimeInterval timeout = 45.0;
    NSDate *deadline = [NSDate dateWithTimeIntervalSinceNow:timeout];
    while ([deadline timeIntervalSinceNow] > 0) {
        NSNumber *downloaded = nil;
        if (@available(iOS 11.0, *)) {
            [url getResourceValue:&downloaded forKey:NSURLUbiquitousItemIsDownloadedKey error:nil];
        }
        if (downloaded.boolValue) return YES;
        [NSThread sleepForTimeInterval:0.20];
    }

    NSNumber *downloaded = nil;
    if (@available(iOS 11.0, *))
        [url getResourceValue:&downloaded forKey:NSURLUbiquitousItemIsDownloadedKey error:nil];
    if (@available(iOS 11.0, *) && downloaded != nil && !downloaded.boolValue) {
        if (error) *error = [NSError errorWithDomain:@"CHUNITHMImport"
                                                  code:22
                                              userInfo:@{NSLocalizedDescriptionKey:
                                                         @"文件仍在 Files/iCloud 中下载，请等待完成后重试"}];
        return NO;
    }
    return YES;
}

static BOOL CopyDocumentStream(NSURL *source, NSString *destination,
                               void (^progress)(long long, long long), NSError **error) {
    NSInputStream *input = [NSInputStream inputStreamWithURL:source];
    // Always replace the destination. A cached import path must never append to
    // bytes left by a previous interrupted or repeated document import.
    NSOutputStream *output = [NSOutputStream outputStreamToFileAtPath:destination append:NO];
    if (input == nil || output == nil) {
        if (error) *error = [NSError errorWithDomain:@"CHUNITHMImport"
                                                  code:10
                                              userInfo:@{NSLocalizedDescriptionKey:
                                                         @"文件提供商没有返回可读取的本地文件副本，请等待文件下载完成后重试"}];
        return NO;
    }
    [input open];
    [output open];
    if (input.streamStatus == NSStreamStatusError ||
        input.streamStatus == NSStreamStatusClosed ||
        output.streamStatus == NSStreamStatusError ||
        output.streamStatus == NSStreamStatusClosed) {
        if (error) *error = input.streamError ?: output.streamError ?:
            [NSError errorWithDomain:@"CHUNITHMImport"
                                 code:11
                             userInfo:@{NSLocalizedDescriptionKey:
                                        @"无法打开所选文件，请确认 Files 中的文件已完成下载"}];
        [input close];
        [output close];
        return NO;
    }
    const NSUInteger bufferSize = 256 * 1024;
    uint8_t *buffer = (uint8_t *)malloc(bufferSize);
    if (buffer == NULL) {
        if (error) *error = [NSError errorWithDomain:@"CHUNITHMImport"
                                                  code:12
                                              userInfo:@{NSLocalizedDescriptionKey: @"无法分配文件读取缓冲区"}];
        [input close];
        [output close];
        return NO;
    }

    long long copied = 0;
    long long lastReported = 0;
    long long total = DocumentSize(source);
    CFAbsoluteTime lastReportTime = 0;
    CFAbsoluteTime lastDataTime = CFAbsoluteTimeGetCurrent();
    BOOL success = YES;
    while (YES) {
        NSInteger read = [input read:buffer maxLength:bufferSize];
        if (read < 0) {
            if (error) *error = input.streamError;
            success = NO;
            break;
        }
        if (read == 0) {
            // NSInputStream can report no bytes while a file provider is still
            // producing data.  A file stream is complete only at end-of-file.
            if (input.streamStatus == NSStreamStatusAtEnd) break;
            if (input.streamStatus == NSStreamStatusError ||
                input.streamStatus == NSStreamStatusClosed) {
                if (error) *error = input.streamError ?: [NSError errorWithDomain:@"CHUNITHMImport"
                                                                               code:15
                                                                           userInfo:@{NSLocalizedDescriptionKey:
                                                                                      @"文件读取中断，请重试"}];
                success = NO;
                break;
            }
            if (CFAbsoluteTimeGetCurrent() - lastDataTime >= 45.0) {
                if (error) *error = [NSError errorWithDomain:@"CHUNITHMImport"
                                                          code:17
                                                      userInfo:@{NSLocalizedDescriptionKey:
                                                                 @"文件提供商长时间没有返回数据，请确认文件已下载到本机后重试"}];
                success = NO;
                break;
            }
            [NSThread sleepForTimeInterval:0.05];
            continue;
        }
        lastDataTime = CFAbsoluteTimeGetCurrent();
        NSInteger written = 0;
        while (written < read) {
            NSInteger count = [output write:buffer + written maxLength:(NSUInteger)(read - written)];
            if (count < 0) {
                if (error) *error = output.streamError;
                success = NO;
                break;
            }
            if (count == 0) {
                if (output.streamStatus == NSStreamStatusError ||
                    output.streamStatus == NSStreamStatusClosed) {
                    if (error) *error = output.streamError ?: [NSError errorWithDomain:@"CHUNITHMImport"
                                                                                     code:16
                                                                                 userInfo:@{NSLocalizedDescriptionKey:
                                                                                            @"文件写入中断，请重试"}];
                    success = NO;
                    break;
                }
                [NSThread sleepForTimeInterval:0.02];
                continue;
            }
            written += count;
        }
        if (!success) break;
        copied += read;
        CFAbsoluteTime now = CFAbsoluteTimeGetCurrent();
        if (progress && (copied - lastReported >= 1024 * 1024 || now - lastReportTime >= 0.35)) {
            progress(copied, total);
            lastReported = copied;
            lastReportTime = now;
        }
    }
    free(buffer);
    [input close];
    [output close];
    if (success && copied == 0) {
        if (error) *error = [NSError errorWithDomain:@"CHUNITHMImport"
                                                  code:13
                                              userInfo:@{NSLocalizedDescriptionKey: @"所选文件为空"}];
        success = NO;
    }
    if (success && total > 0 && copied != total) {
        if (error) *error = [NSError errorWithDomain:@"CHUNITHMImport"
                                                  code:18
                                              userInfo:@{NSLocalizedDescriptionKey:
                                                         @"文件复制不完整，请等待 Files 中的上传或下载结束后重试"}];
        success = NO;
    }
    if (success && progress && copied != lastReported) progress(copied, total > 0 ? total : copied);
    return success;
}

static void RemovePath(NSString *path) {
    if (path.length > 0)
        [[NSFileManager defaultManager] removeItemAtPath:path error:nil];
}

static void CopySingleDocument(NSURL *url, NSString *mode) {
    (void)mode;
    BOOL accessing = [url startAccessingSecurityScopedResource];
    NSString *name = SafeFileName(url.lastPathComponent, @"import.zip");
    NSString *stamp = [NSString stringWithFormat:@"%lld", (long long)(NSDate.date.timeIntervalSince1970 * 1000)];
    NSString *extension = name.pathExtension.length > 0 ? name.pathExtension : @"zip";
    NSString *destination = [[CacheImportsDirectory() stringByAppendingPathComponent:stamp]
        stringByAppendingPathExtension:extension];
    long long total = DocumentSize(url);

    dispatch_async(dispatch_get_main_queue(), ^{
        SendUnityMessage(@"OnImportStarted", name);
        SendUnityMessage(@"OnImportCopyProgress", [NSString stringWithFormat:@"0|%lld", total]);
    });

    __block NSError *error = nil;
    __block BOOL copied = NO;
    NSFileCoordinator *coordinator = [[NSFileCoordinator alloc] initWithFilePresenter:nil];
    if (WaitForDocumentAvailability(url, &error)) {
        [coordinator coordinateReadingItemAtURL:url options:NSFileCoordinatorReadingWithoutChanges
                                           error:&error byAccessor:^(NSURL *coordinatedURL) {
            if (!WaitForDocumentAvailability(coordinatedURL, &error)) return;
            copied = CopyDocumentStream(coordinatedURL, destination, ^(long long bytes, long long bytesTotal) {
                dispatch_async(dispatch_get_main_queue(), ^{
                    SendUnityMessage(@"OnImportCopyProgress", [NSString stringWithFormat:@"%lld|%lld", bytes, bytesTotal]);
                });
            }, &error);
        }];
    }
    if (!copied && error == nil) {
        error = [NSError errorWithDomain:@"CHUNITHMImport" code:14
                                 userInfo:@{NSLocalizedDescriptionKey:
                                            @"文件提供商未能完成读取，请检查 iCloud/Files 中的下载状态后重试"}];
    }

    if (accessing) [url stopAccessingSecurityScopedResource];
    dispatch_async(dispatch_get_main_queue(), ^{
        if (copied) {
            // Unity decides whether this ZIP is a song pack or a gameplay skin
            // from the pending import mode it already keeps in AppUiShell.
            SendUnityMessage(@"OnImportFilePicked", destination);
        } else {
            RemovePath(destination);
            SendUnityMessage(@"OnImportCopyFailed", error.localizedDescription ?: @"无法读取所选文件");
        }
    });
}

// keyWindow is unreliable when the app is running with a scene-based
// lifecycle. Resolve the foreground Unity window first, then walk through
// presented/navigation/tab controllers before presenting the picker.
static UIViewController *ActiveRootViewController(void) {
    UIWindow *window = nil;
    if (@available(iOS 13.0, *)) {
        for (UIScene *scene in UIApplication.sharedApplication.connectedScenes) {
            if (scene.activationState != UISceneActivationStateForegroundActive &&
                scene.activationState != UISceneActivationStateForegroundInactive)
                continue;
            if (![scene isKindOfClass:[UIWindowScene class]]) continue;
            UIWindowScene *windowScene = (UIWindowScene *)scene;
            for (UIWindow *candidate in windowScene.windows) {
                if (candidate.isKeyWindow) { window = candidate; break; }
            }
            if (window == nil) window = windowScene.windows.firstObject;
            if (window != nil) break;
        }
    }
    if (window == nil) window = UIApplication.sharedApplication.keyWindow;
    UIViewController *controller = window.rootViewController;
    while (controller.presentedViewController != nil)
        controller = controller.presentedViewController;
    while ([controller isKindOfClass:[UINavigationController class]])
        controller = [(UINavigationController *)controller visibleViewController];
    while ([controller isKindOfClass:[UITabBarController class]])
        controller = [(UITabBarController *)controller selectedViewController];
    return controller;
}

static void CopyImages(NSArray<NSURL *> *urls) {
    NSString *directory = [CacheImportsDirectory() stringByAppendingPathComponent:
        [NSString stringWithFormat:@"images-%lld", (long long)(NSDate.date.timeIntervalSince1970 * 1000)]];
    [[NSFileManager defaultManager] createDirectoryAtPath:directory
                              withIntermediateDirectories:YES
                                               attributes:nil
                                                    error:nil];
    dispatch_async(dispatch_get_main_queue(), ^{
        SendUnityMessage(@"OnBackgroundImagesStarted", @(urls.count).stringValue);
    });

    NSMutableArray<NSString *> *paths = [NSMutableArray arrayWithCapacity:urls.count];
    for (NSUInteger index = 0; index < urls.count; index++) {
        NSURL *url = urls[index];
        BOOL accessing = [url startAccessingSecurityScopedResource];
        NSString *name = SafeFileName(url.lastPathComponent, [NSString stringWithFormat:@"source_%02lu.png", (unsigned long)index]);
        NSString *extension = name.pathExtension.length > 0 ? name.pathExtension : @"png";
        NSString *fileName = [NSString stringWithFormat:@"source_%02lu.%@", (unsigned long)index, extension];
        NSString *destination = [directory stringByAppendingPathComponent:fileName];
        __block NSError *error = nil;
        __block BOOL copied = NO;
        NSFileCoordinator *coordinator = [[NSFileCoordinator alloc] initWithFilePresenter:nil];
        if (WaitForDocumentAvailability(url, &error)) {
            [coordinator coordinateReadingItemAtURL:url options:NSFileCoordinatorReadingWithoutChanges
                                               error:&error byAccessor:^(NSURL *coordinatedURL) {
                if (!WaitForDocumentAvailability(coordinatedURL, &error)) return;
                copied = CopyDocumentStream(coordinatedURL, destination, ^(long long bytes, long long total) {
                    NSString *payload = [NSString stringWithFormat:@"%lu|%lu|%lld|%lld|%@",
                        (unsigned long)index, (unsigned long)urls.count, bytes, total, name];
                    dispatch_async(dispatch_get_main_queue(), ^{
                        SendUnityMessage(@"OnBackgroundImageCopyProgress", payload);
                    });
                }, &error);
            }];
        }
        if (!copied && error == nil) {
            error = [NSError errorWithDomain:@"CHUNITHMImport" code:14
                                     userInfo:@{NSLocalizedDescriptionKey:
                                                @"图片文件尚未完成下载，请等待 Files 中的上传/下载状态结束后重试"}];
        }
        if (accessing) [url stopAccessingSecurityScopedResource];
        if (!copied) {
            RemovePath(destination);
            dispatch_async(dispatch_get_main_queue(), ^{
                SendUnityMessage(@"OnBackgroundImageCopyFailed", error.localizedDescription ?: @"无法读取图片");
            });
            RemovePath(directory);
            return;
        }
        [paths addObject:destination];
    }

    dispatch_async(dispatch_get_main_queue(), ^{
        SendUnityMessage(@"OnBackgroundImagesPicked", [paths componentsJoinedByString:@"\n"]);
    });
}

@implementation ChunithmDocumentPickerDelegate

- (void)documentPicker:(UIDocumentPickerViewController *)controller
 didPickDocumentsAtURLs:(NSArray<NSURL *> *)urls {
    if (urls.count == 0) {
        SendUnityMessage([self.mode isEqualToString:@"images"] ? @"OnBackgroundImagesPicked" : @"OnImportFilePicked", @"");
        return;
    }
    NSString *mode = self.mode ?: @"zip";
    if ([mode isEqualToString:@"images"]) {
        if (urls.count > 50) {
            SendUnityMessage(@"OnBackgroundImageCopyFailed", @"最多只能导入 50 张图片");
            return;
        }
        NSArray<NSURL *> *selected = urls;
        dispatch_async(dispatch_get_global_queue(QOS_CLASS_UTILITY, 0), ^{
            CopyImages(selected);
        });
    } else {
        NSURL *selected = urls.firstObject;
        dispatch_async(dispatch_get_global_queue(QOS_CLASS_UTILITY, 0), ^{
            CopySingleDocument(selected, mode);
        });
    }
}

- (void)documentPickerWasCancelled:(UIDocumentPickerViewController *)controller {
    if ([self.mode isEqualToString:@"images"])
        SendUnityMessage(@"OnBackgroundImagesPicked", @"");
    else
        SendUnityMessage(@"OnImportFilePicked", @"");
}

@end

extern "C" void ChunithmOpenDocumentPicker(const char *mode) {
    NSString *requestedMode = mode ? [NSString stringWithUTF8String:mode] : @"zip";
    dispatch_async(dispatch_get_main_queue(), ^{
        UIViewController *root = ActiveRootViewController();
        if (root == nil) {
            SendUnityMessage([requestedMode isEqualToString:@"images"]
                ? @"OnBackgroundImageCopyFailed" : @"OnImportCopyFailed",
                @"无法找到 iOS 文件选择器窗口");
            return;
        }

        BOOL images = [requestedMode isEqualToString:@"images"];
        UIDocumentPickerViewController *picker = nil;
        if (@available(iOS 14.0, *)) {
            NSArray<UTType *> *types = images
                ? @[UTTypeImage]
                : @[UTTypeZIP, UTTypeData];
            // Import-as-copy asks Files/iCloud to materialize the remote item
            // before returning. Open mode can return a provider URL that is
            // still uploading/downloading and cannot be streamed reliably.
            picker = [[UIDocumentPickerViewController alloc]
                initForOpeningContentTypes:types asCopy:YES];
        } else {
            NSArray<NSString *> *types = images
                ? @[@"public.image"]
                : @[@"public.zip-archive", @"com.pkware.zip-archive", @"public.data"];
            picker = [[UIDocumentPickerViewController alloc]
                initWithDocumentTypes:types inMode:UIDocumentPickerModeImport];
        }
        gPickerDelegate = [ChunithmDocumentPickerDelegate new];
        gPickerDelegate.mode = requestedMode;
        picker.delegate = gPickerDelegate;
        picker.allowsMultipleSelection = images;
        picker.modalPresentationStyle = UIModalPresentationFullScreen;
        [root presentViewController:picker animated:YES completion:nil];
    });
}

extern "C" void ChunithmShareFile(const char *path) {
    NSString *filePath = path ? [NSString stringWithUTF8String:path] : @"";
    dispatch_async(dispatch_get_main_queue(), ^{
        if (filePath.length == 0 || ![[NSFileManager defaultManager] fileExistsAtPath:filePath])
            return;
        UIViewController *root = UIApplication.sharedApplication.keyWindow.rootViewController;
        while (root.presentedViewController != nil) root = root.presentedViewController;
        if (root == nil) return;

        NSURL *url = [NSURL fileURLWithPath:filePath];
        UIActivityViewController *share = [[UIActivityViewController alloc]
            initWithActivityItems:@[url] applicationActivities:nil];
        UIPopoverPresentationController *popover = share.popoverPresentationController;
        if (popover != nil) {
            popover.sourceView = root.view;
            popover.sourceRect = CGRectMake(CGRectGetMidX(root.view.bounds),
                CGRectGetMidY(root.view.bounds), 1, 1);
            popover.permittedArrowDirections = 0;
        }
        [root presentViewController:share animated:YES completion:nil];
    });
}

extern "C" long long ChunithmGetAvailableDiskSpace(const char *path) {
    NSString *requestedPath = path ? [NSString stringWithUTF8String:path] : @"";
    if (requestedPath.length == 0) requestedPath = NSHomeDirectory();
    NSError *error = nil;
    NSDictionary<NSFileAttributeKey, id> *attributes =
        [[NSFileManager defaultManager] attributesOfFileSystemForPath:requestedPath error:&error];
    NSNumber *available = attributes[NSFileSystemFreeSize];
    return error == nil && available != nil ? available.longLongValue : -1;
}

extern "C" long long ChunithmGetAvailableMemory(void) {
#if __has_include(<os/proc.h>)
    return (long long)os_proc_available_memory();
#else
    return -1;
#endif
}
