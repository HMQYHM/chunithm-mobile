#import <Foundation/Foundation.h>

// iOS multiplayer WebSocket transport.
//
// Unity's managed ClientWebSocket can fail before the HTTP upgrade on some
// IL2CPP/iOS combinations and then reports only a generic transport error.
// NSURLSessionWebSocketTask uses the system TLS trust store, proxy settings and
// current iOS networking implementation while preserving the same JSON wire
// protocol used by Android and the server.

static NSString * const kUnityRuntimeObject = @"CHUNITHM Mobile Runtime";

extern "C" void UnitySendMessage(const char *objectName, const char *methodName, const char *message);

static void SendRuntimeMessage(NSString *method, NSString *value) {
    dispatch_async(dispatch_get_main_queue(), ^{
        UnitySendMessage(kUnityRuntimeObject.UTF8String, method.UTF8String,
                         (value ?: @"").UTF8String);
    });
}

static NSString *WebSocketErrorText(NSError *error) {
    if (error == nil) return @"未知网络错误";
    NSString *description = error.localizedDescription ?: @"未知网络错误";
    return [NSString stringWithFormat:@"%@ (%@/%ld)", description,
            error.domain ?: @"NSError", (long)error.code];
}

@interface ChunithmWebSocketDelegate : NSObject <NSURLSessionWebSocketDelegate>
@property(nonatomic) NSInteger generation;
@property(nonatomic) BOOL terminalMessageSent;
@property(nonatomic, strong) NSURLSession *session;
@property(nonatomic, strong) NSURLSessionWebSocketTask *task;
- (void)beginReceiving;
@end

static NSInteger gWebSocketGeneration = 0;
static ChunithmWebSocketDelegate *gWebSocketDelegate = nil;

@implementation ChunithmWebSocketDelegate

- (BOOL)isCurrent {
    return self.generation == gWebSocketGeneration && gWebSocketDelegate == self;
}

- (void)reportError:(NSError *)error {
    if (![self isCurrent] || self.terminalMessageSent) return;
    self.terminalMessageSent = YES;
    SendRuntimeMessage(@"OnIosWebSocketError", WebSocketErrorText(error));
}

- (void)beginReceiving {
    if (![self isCurrent] || self.task == nil) return;
    __weak ChunithmWebSocketDelegate *weakSelf = self;
    [self.task receiveMessageWithCompletionHandler:^(NSURLSessionWebSocketMessage *message,
                                                       NSError *error) {
        ChunithmWebSocketDelegate *strongSelf = weakSelf;
        if (strongSelf == nil || ![strongSelf isCurrent]) return;
        if (error != nil) {
            [strongSelf reportError:error];
            return;
        }
        NSString *text = message.string;
        if (text == nil && message.data != nil)
            text = [[NSString alloc] initWithData:message.data encoding:NSUTF8StringEncoding];
        if (text != nil) SendRuntimeMessage(@"OnIosWebSocketMessage", text);
        [strongSelf beginReceiving];
    }];
}

- (void)URLSession:(NSURLSession *)session webSocketTask:(NSURLSessionWebSocketTask *)webSocketTask
 didOpenWithProtocol:(NSString *)protocol {
    if (![self isCurrent] || webSocketTask != self.task) return;
    SendRuntimeMessage(@"OnIosWebSocketConnected", @"");
    [self beginReceiving];
}

- (void)URLSession:(NSURLSession *)session webSocketTask:(NSURLSessionWebSocketTask *)webSocketTask
 didCloseWithCode:(NSURLSessionWebSocketCloseCode)closeCode reason:(NSData *)reason {
    if (![self isCurrent] || webSocketTask != self.task || self.terminalMessageSent) return;
    self.terminalMessageSent = YES;
    NSString *text = reason.length > 0
        ? [[NSString alloc] initWithData:reason encoding:NSUTF8StringEncoding]
        : @"";
    SendRuntimeMessage(@"OnIosWebSocketClosed",
        [NSString stringWithFormat:@"%ld|%@", (long)closeCode, text ?: @""]);
}

- (void)URLSession:(NSURLSession *)session task:(NSURLSessionTask *)task
 didCompleteWithError:(NSError *)error {
    if (error != nil) [self reportError:error];
}

@end

static void CloseCurrentWebSocket(void) {
    gWebSocketGeneration++;
    ChunithmWebSocketDelegate *current = gWebSocketDelegate;
    gWebSocketDelegate = nil;
    if (current == nil) return;
    [current.task cancelWithCloseCode:NSURLSessionWebSocketCloseCodeNormalClosure reason:nil];
    [current.session invalidateAndCancel];
    current.task = nil;
    current.session = nil;
}

extern "C" void ChunithmWebSocketConnect(const char *urlValue) {
    NSString *text = urlValue ? [NSString stringWithUTF8String:urlValue] : @"";
    dispatch_async(dispatch_get_main_queue(), ^{
        CloseCurrentWebSocket();
        NSURL *url = [NSURL URLWithString:text];
        if (url == nil) {
            SendRuntimeMessage(@"OnIosWebSocketError", @"WebSocket 地址无效 (NSURL/-1)");
            return;
        }

        NSInteger generation = ++gWebSocketGeneration;
        ChunithmWebSocketDelegate *delegate = [ChunithmWebSocketDelegate new];
        delegate.generation = generation;
        NSURLSessionConfiguration *configuration = [NSURLSessionConfiguration defaultSessionConfiguration];
        configuration.requestCachePolicy = NSURLRequestReloadIgnoringLocalCacheData;
        configuration.timeoutIntervalForRequest = 15.0;
        configuration.timeoutIntervalForResource = 30.0;
        configuration.waitsForConnectivity = NO;
        NSOperationQueue *queue = [NSOperationQueue new];
        queue.maxConcurrentOperationCount = 1;
        delegate.session = [NSURLSession sessionWithConfiguration:configuration
                                                          delegate:delegate
                                                     delegateQueue:queue];
        delegate.task = [delegate.session webSocketTaskWithURL:url];
        gWebSocketDelegate = delegate;
        [delegate.task resume];
    });
}

extern "C" void ChunithmWebSocketSend(const char *messageValue) {
    NSString *text = messageValue ? [NSString stringWithUTF8String:messageValue] : @"";
    dispatch_async(dispatch_get_main_queue(), ^{
        ChunithmWebSocketDelegate *delegate = gWebSocketDelegate;
        if (delegate == nil || ![delegate isCurrent] || delegate.task == nil) return;
        NSURLSessionWebSocketMessage *message =
            [[NSURLSessionWebSocketMessage alloc] initWithString:text ?: @""];
        [delegate.task sendMessage:message completionHandler:^(NSError *error) {
            if (error != nil) [delegate reportError:error];
        }];
    });
}

extern "C" void ChunithmWebSocketClose(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        CloseCurrentWebSocket();
    });
}
