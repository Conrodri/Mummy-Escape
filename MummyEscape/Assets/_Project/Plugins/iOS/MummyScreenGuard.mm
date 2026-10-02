// Screen capture detection for the start-of-run map preview (see ScreenGuard.cs).
// iOS cannot block screenshots: we count them (UIApplicationUserDidTakeScreenshotNotification) and report whether
// the screen is being recorded or mirrored (UIScreen.isCaptured), the game reacts by hiding / redrawing the tomb.
#import <UIKit/UIKit.h>

static int sScreenshotCount = 0;
static id sObserver = nil;

extern "C" {

void _MummyGuard_Start()
{
    if (sObserver != nil) return;
    sObserver = [[NSNotificationCenter defaultCenter]
        addObserverForName:UIApplicationUserDidTakeScreenshotNotification
                    object:nil
                     queue:[NSOperationQueue mainQueue]
                usingBlock:^(NSNotification *note) { sScreenshotCount++; }];
}

int _MummyGuard_ScreenshotCount()
{
    return sScreenshotCount;
}

bool _MummyGuard_IsCaptured()
{
    if (@available(iOS 11.0, *)) return [UIScreen mainScreen].isCaptured;
    return false;
}

}
