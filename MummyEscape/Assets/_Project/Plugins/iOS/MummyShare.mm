#import <UIKit/UIKit.h>

extern UIViewController* UnityGetGLViewController();

// Native share sheet for Mummy Escape results (called from ShareService.cs).
extern "C" void _MummyShareText(const char* text)
{
    NSString* message = [NSString stringWithUTF8String:text];
    UIActivityViewController* sheet = [[UIActivityViewController alloc] initWithActivityItems:@[message]
                                                                        applicationActivities:nil];
    UIViewController* root = UnityGetGLViewController();
    // iPad requires an anchor for the popover.
    sheet.popoverPresentationController.sourceView = root.view;
    sheet.popoverPresentationController.sourceRect = CGRectMake(root.view.bounds.size.width / 2, root.view.bounds.size.height, 1, 1);
    [root presentViewController:sheet animated:YES completion:nil];
}
