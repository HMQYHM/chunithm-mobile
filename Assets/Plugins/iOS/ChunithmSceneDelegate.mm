#import "UnityAppController.h"

// UIKit creates a separate scene delegate. Reusing UnityAppController here
// would replace Unity's global controller with an uninitialized second instance.
@interface UnityAppController (ChunithmScenes)
- (void)initUnityWithApplication:(UIApplication*)application;
- (void)connectUnityWindowScene:(UIWindowScene*)scene;
@end

@implementation UnityAppController (ChunithmScenes)
- (void)connectUnityWindowScene:(UIWindowScene*)scene
{
    if (_window == nil)
        _window = [[UIWindow alloc] initWithWindowScene:scene];
    else
        _window.windowScene = scene;
    [self initUnityWithApplication:UIApplication.sharedApplication];
}
@end

@interface ChunithmSceneDelegate : NSObject<UIWindowSceneDelegate>
@property(nonatomic, strong) UIWindow *window;
@end

@implementation ChunithmSceneDelegate
- (void)scene:(UIScene*)scene willConnectToSession:(UISceneSession*)session options:(UISceneConnectionOptions*)options
{
    if (![scene isKindOfClass:UIWindowScene.class]) return;
    [GetAppController() connectUnityWindowScene:(UIWindowScene*)scene];
    self.window = GetAppController().window;
}
- (void)sceneDidBecomeActive:(UIScene*)scene
{
    [GetAppController() applicationDidBecomeActive:UIApplication.sharedApplication];
}
- (void)sceneWillResignActive:(UIScene*)scene
{
    [GetAppController() applicationWillResignActive:UIApplication.sharedApplication];
}
- (void)sceneWillEnterForeground:(UIScene*)scene
{
    [GetAppController() applicationWillEnterForeground:UIApplication.sharedApplication];
}
- (void)sceneDidEnterBackground:(UIScene*)scene
{
    [GetAppController() applicationDidEnterBackground:UIApplication.sharedApplication];
}
@end
